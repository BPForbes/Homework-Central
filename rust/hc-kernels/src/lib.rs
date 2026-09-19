//! C ABI for the chat-monitor kernels the API loads at runtime.
//!
//! Lexical bins, store cosine, GEMV, expertise hash, HashEmbed, JSON
//! batch cosine, support-set cosine, and the FIFO-free LRU (`hc-cache`)
//! cross this boundary. Encode metadata, hashed-MLP train/replay, and
//! the rest of the API stay in C#.
//! The Docker image does not ship `rustc`; C# falls back when this
//! library is absent or a newer export is missing.

use hc_feature_encode::{
    add_expertise_hash, add_weighted_tokens, embed_text, hash_embed, HASH_EMBED_BINS,
    STRUCTURAL_FEATURE_COUNT,
};
use hc_cache::LruCache;
use hc_gemv::{multiply_bias, multiply_transpose};
use hc_vector_cosine::{batch_cosine_json, cosine, max_support_cosine, support_cosine};
use std::ffi::c_void;
use std::sync::Mutex;

/// Writes 86 lexical bins for `text` into `output`. Returns 0 on success.
///
/// # Safety
/// `text` must be `text_len` UTF-8 bytes when `text_len` is nonzero. `output`
/// must hold `output_len` floats.
#[no_mangle]
pub unsafe extern "C" fn hc_embed_text(
    text: *const u8,
    text_len: usize,
    output: *mut f32,
    output_len: usize,
) -> i32 {
    if output.is_null() || output_len < STRUCTURAL_FEATURE_COUNT {
        return -1;
    }
    if text_len > 0 && text.is_null() {
        return -1;
    }

    let text = if text_len == 0 {
        ""
    } else {
        let bytes = unsafe { std::slice::from_raw_parts(text, text_len) };
        match std::str::from_utf8(bytes) {
            Ok(value) => value,
            Err(_) => return -2,
        }
    };

    let values = embed_text(text);
    unsafe {
        std::ptr::copy_nonoverlapping(values.as_ptr(), output, STRUCTURAL_FEATURE_COUNT);
    }
    0
}

/// Adds weighted unigram/bigram bins into an existing structural buffer.
///
/// # Safety
/// `text` must be `text_len` UTF-8 bytes when `text_len` is nonzero. `values`
/// must hold `values_len` floats.
#[no_mangle]
pub unsafe extern "C" fn hc_add_weighted_tokens(
    values: *mut f32,
    values_len: usize,
    text: *const u8,
    text_len: usize,
    weight: f32,
) -> i32 {
    if values.is_null() || values_len < STRUCTURAL_FEATURE_COUNT {
        return -1;
    }
    if text_len > 0 && text.is_null() {
        return -1;
    }

    let text = if text_len == 0 {
        ""
    } else {
        let bytes = unsafe { std::slice::from_raw_parts(text, text_len) };
        match std::str::from_utf8(bytes) {
            Ok(value) => value,
            Err(_) => return -2,
        }
    };

    let slots = unsafe { std::slice::from_raw_parts_mut(values, values_len) };
    add_weighted_tokens(slots, text, weight);
    0
}

/// Cosine of the overlapping prefix. Empty or invalid pointers score `0`.
///
/// # Safety
/// `left` / `right` must be `left_len` / `right_len` floats when non-empty.
#[no_mangle]
pub unsafe extern "C" fn hc_cosine(
    left: *const f32,
    left_len: usize,
    right: *const f32,
    right_len: usize,
) -> f64 {
    if left_len == 0 || right_len == 0 || left.is_null() || right.is_null() {
        return 0.0;
    }

    let left_slots = unsafe { std::slice::from_raw_parts(left, left_len) };
    let right_slots = unsafe { std::slice::from_raw_parts(right, right_len) };
    cosine(left_slots, right_slots)
}

/// Column-major `y = W x + b`. Returns 0 on success.
///
/// # Safety
/// `weights` is `rows * cols` floats. `source` is `cols`, `biases` and
/// `destination` are `rows`.
#[no_mangle]
pub unsafe extern "C" fn hc_gemv_bias(
    weights: *const f32,
    rows: usize,
    cols: usize,
    source: *const f32,
    biases: *const f32,
    destination: *mut f32,
) -> i32 {
    let Some(weight_count) = rows.checked_mul(cols) else {
        return -1;
    };
    if rows == 0
        || cols == 0
        || weights.is_null()
        || source.is_null()
        || biases.is_null()
        || destination.is_null()
    {
        return -1;
    }

    let weight_slots = unsafe { std::slice::from_raw_parts(weights, weight_count) };
    let source_slots = unsafe { std::slice::from_raw_parts(source, cols) };
    let bias_slots = unsafe { std::slice::from_raw_parts(biases, rows) };
    let destination_slots = unsafe { std::slice::from_raw_parts_mut(destination, rows) };
    if multiply_bias(
        weight_slots,
        rows,
        cols,
        source_slots,
        bias_slots,
        destination_slots,
    ) {
        0
    } else {
        -1
    }
}

/// Column-major `destination = Wᵀ delta`. Returns 0 on success.
///
/// # Safety
/// `weights` is `rows * cols` floats. `delta` is `rows`, `destination` is `cols`.
#[no_mangle]
pub unsafe extern "C" fn hc_gemv_transpose(
    weights: *const f32,
    rows: usize,
    cols: usize,
    delta: *const f32,
    destination: *mut f32,
) -> i32 {
    let Some(weight_count) = rows.checked_mul(cols) else {
        return -1;
    };
    if rows == 0 || cols == 0 || weights.is_null() || delta.is_null() || destination.is_null() {
        return -1;
    }

    let weight_slots = unsafe { std::slice::from_raw_parts(weights, weight_count) };
    let delta_slots = unsafe { std::slice::from_raw_parts(delta, rows) };
    let destination_slots = unsafe { std::slice::from_raw_parts_mut(destination, cols) };
    if multiply_transpose(weight_slots, rows, cols, delta_slots, destination_slots) {
        0
    } else {
        -1
    }
}

/// Adds one tutoring expertise label into the hashed bins.
///
/// # Safety
/// `values` must hold `values_len` floats. `label` is `label_len` UTF-8 bytes
/// when `label_len` is nonzero.
#[no_mangle]
pub unsafe extern "C" fn hc_add_expertise_hash(
    values: *mut f32,
    values_len: usize,
    label: *const u8,
    label_len: usize,
    base_input_size: usize,
    bin_count: usize,
) -> i32 {
    if values.is_null() || bin_count == 0 {
        return -1;
    }
    let Some(required) = base_input_size.checked_add(bin_count) else {
        return -1;
    };
    if values_len < required {
        return -1;
    }

    let label = match unsafe { utf8_or_empty(label, label_len) } {
        Ok(text) => text,
        Err(status) => return status,
    };
    let slots = unsafe { std::slice::from_raw_parts_mut(values, values_len) };
    if add_expertise_hash(slots, label, base_input_size, bin_count) {
        0
    } else {
        -1
    }
}

/// 64-bin HashEmbed used when Ollama is down.
///
/// # Safety
/// `text` is `text_len` UTF-8 bytes when nonzero. `output` holds `output_len`
/// floats and must be at least 64.
#[no_mangle]
pub unsafe extern "C" fn hc_hash_embed(
    text: *const u8,
    text_len: usize,
    output: *mut f32,
    output_len: usize,
) -> i32 {
    if output.is_null() || output_len < HASH_EMBED_BINS {
        return -1;
    }
    let text = match unsafe { utf8_or_empty(text, text_len) } {
        Ok(value) => value,
        Err(status) => return status,
    };
    let values = hash_embed(text);
    unsafe {
        std::ptr::copy_nonoverlapping(values.as_ptr(), output, HASH_EMBED_BINS);
    }
    0
}

/// Support-set cosine (clamped `[0, 1]`). Empty or null pointers score `0`.
///
/// # Safety
/// `left` / `right` must be `left_len` / `right_len` floats when non-empty.
#[no_mangle]
pub unsafe extern "C" fn hc_support_cosine(
    left: *const f32,
    left_len: usize,
    right: *const f32,
    right_len: usize,
) -> f64 {
    if left_len == 0 || right_len == 0 || left.is_null() || right.is_null() {
        return 0.0;
    }

    let left_slots = unsafe { std::slice::from_raw_parts(left, left_len) };
    let right_slots = unsafe { std::slice::from_raw_parts(right, right_len) };
    support_cosine(left_slots, right_slots)
}

/// Max support cosine over concatenated vectors.
///
/// # Safety
/// `packed` is `packed_len` floats. `lengths` is `count` widths that sum to
/// `packed_len`.
#[no_mangle]
pub unsafe extern "C" fn hc_support_max_cosine(
    query: *const f32,
    query_len: usize,
    packed: *const f32,
    packed_len: usize,
    lengths: *const usize,
    count: usize,
) -> f64 {
    if query_len == 0 || query.is_null() {
        return 0.0;
    }
    if count == 0 {
        return 0.0;
    }
    if packed_len > 0 && packed.is_null() || lengths.is_null() {
        return 0.0;
    }

    let query_slots = unsafe { std::slice::from_raw_parts(query, query_len) };
    let packed_slots = if packed_len == 0 {
        &[] as &[f32]
    } else {
        unsafe { std::slice::from_raw_parts(packed, packed_len) }
    };
    let length_slots = unsafe { std::slice::from_raw_parts(lengths, count) };
    max_support_cosine(query_slots, packed_slots, length_slots).unwrap_or(0.0)
}

/// Store cosine of one query against concatenated JSON embedding arrays.
///
/// # Safety
/// `json` is `json_len` UTF-8 bytes (concatenation). `lengths` is `doc_count`
/// blob sizes that sum to `json_len`. `scores` holds `doc_count` doubles.
#[no_mangle]
pub unsafe extern "C" fn hc_batch_cosine_json(
    query: *const f32,
    query_len: usize,
    json: *const u8,
    json_len: usize,
    lengths: *const usize,
    doc_count: usize,
    scores: *mut f64,
) -> i32 {
    if doc_count == 0 {
        return 0;
    }
    if scores.is_null() {
        return -1;
    }
    if lengths.is_null() || query_len > 0 && query.is_null() || json_len > 0 && json.is_null() {
        return -1;
    }

    let query_slots = if query_len == 0 {
        &[] as &[f32]
    } else {
        unsafe { std::slice::from_raw_parts(query, query_len) }
    };
    let json_bytes = if json_len == 0 {
        &[] as &[u8]
    } else {
        unsafe { std::slice::from_raw_parts(json, json_len) }
    };
    let length_slots = unsafe { std::slice::from_raw_parts(lengths, doc_count) };
    let expected = match length_slots.iter().try_fold(0usize, |total, length| {
        total.checked_add(*length)
    }) {
        Some(total) => total,
        None => return -1,
    };
    if expected != json_len {
        return -1;
    }

    let mut blobs = Vec::with_capacity(doc_count);
    let mut offset = 0usize;
    for length in length_slots {
        let end = offset + *length;
        let slice = &json_bytes[offset..end];
        match std::str::from_utf8(slice) {
            Ok(text) => blobs.push(text),
            Err(_) => return -2,
        }
        offset = end;
    }

    let computed = batch_cosine_json(query_slots, &blobs);
    if computed.len() != doc_count {
        return -1;
    }
    let score_slots = unsafe { std::slice::from_raw_parts_mut(scores, doc_count) };
    score_slots.copy_from_slice(&computed);
    0
}

/// Spill when the CLR-sampled heap is at or above the watermark, or when process
/// RSS has reached 70% of the reported limit. Returns `1` (spill), `0` (hold),
/// or `-1` (negative inputs). This crate does not read the .NET GC heap; C#
/// passes [`GCMemoryInfo`](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo)
/// samples.
#[no_mangle]
pub extern "C" fn hc_heap_should_spill(
    used_bytes: i64,
    high_watermark_bytes: i64,
    process_rss_bytes: i64,
    process_limit_bytes: i64,
) -> i32 {
    if used_bytes < 0
        || high_watermark_bytes < 0
        || process_rss_bytes < 0
        || process_limit_bytes < 0
    {
        return -1;
    }
    if high_watermark_bytes > 0 && used_bytes >= high_watermark_bytes {
        return 1;
    }
    if process_limit_bytes > 0
        && process_rss_bytes >= ((process_limit_bytes as f64) * 0.70) as i64
    {
        return 1;
    }
    0
}

/// Bounded top-K by absolute value. Writes at most `take` indexes/values,
/// largest-abs first. Skips non-finite values and `|v| <= 1e-6`.
///
/// Uses a min-heap of size `take` so the working set is O(k), not O(n).
/// See [`BinaryHeap`](https://doc.rust-lang.org/stable/std/collections/struct.BinaryHeap.html).
///
/// # Safety
/// `values` and `indexes` must hold `count` elements when `count` is nonzero.
/// `out_indexes` and `out_values` must hold `take` elements when `take` is nonzero.
#[no_mangle]
pub unsafe extern "C" fn hc_heap_top_k_abs(
    values: *const f32,
    indexes: *const i32,
    count: usize,
    take: usize,
    out_indexes: *mut i32,
    out_values: *mut f32,
) -> i32 {
    if take == 0 {
        return 0;
    }
    if out_indexes.is_null() || out_values.is_null() {
        return -1;
    }
    if count > 0 && (values.is_null() || indexes.is_null()) {
        return -1;
    }

    let values = if count == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(values, count) }
    };
    let indexes = if count == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(indexes, count) }
    };

    let mut heap: std::collections::BinaryHeap<std::cmp::Reverse<AbsEntry>> =
        std::collections::BinaryHeap::with_capacity(take.min(count).saturating_add(1));
    for i in 0..count {
        let abs = values[i].abs();
        if !abs.is_finite() || abs <= 1e-6 {
            continue;
        }
        let entry = AbsEntry {
            abs_bits: abs.to_bits(),
            index: indexes[i],
        };
        if heap.len() < take {
            heap.push(std::cmp::Reverse(entry));
        } else if let Some(std::cmp::Reverse(worst)) = heap.peek() {
            if abs.to_bits() > worst.abs_bits {
                heap.pop();
                heap.push(std::cmp::Reverse(entry));
            }
        }
    }

    let mut items: Vec<AbsEntry> = heap.into_iter().map(|std::cmp::Reverse(entry)| entry).collect();
    items.sort_by(|left, right| right.abs_bits.cmp(&left.abs_bits));
    let written = items.len();
    let out_i = unsafe { std::slice::from_raw_parts_mut(out_indexes, take) };
    let out_v = unsafe { std::slice::from_raw_parts_mut(out_values, take) };
    for (slot, item) in items.into_iter().enumerate() {
        out_i[slot] = item.index;
        out_v[slot] = f32::from_bits(item.abs_bits);
    }
    written as i32
}

#[derive(Copy, Clone, Eq, PartialEq)]
struct AbsEntry {
    abs_bits: u32,
    index: i32,
}

impl Ord for AbsEntry {
    fn cmp(&self, other: &Self) -> std::cmp::Ordering {
        self.abs_bits
            .cmp(&other.abs_bits)
            .then(self.index.cmp(&other.index))
    }
}

impl PartialOrd for AbsEntry {
    fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> {
        Some(self.cmp(other))
    }
}

struct NativeLru {
    cache: Mutex<LruCache<Vec<u8>, Vec<u8>>>,
}

/// Creates an LRU. Capacity `0` is a valid empty cache. The pointer is
/// owned by the caller and must be passed to [`hc_lru_free`].
#[no_mangle]
pub extern "C" fn hc_lru_create(capacity: usize) -> *mut c_void {
    Box::into_raw(Box::new(NativeLru {
        cache: Mutex::new(LruCache::new(capacity)),
    })) as *mut c_void
}

/// # Safety
/// `handle` must be from [`hc_lru_create`] or null.
#[no_mangle]
pub unsafe extern "C" fn hc_lru_free(handle: *mut c_void) {
    if handle.is_null() {
        return;
    }
    drop(unsafe { Box::from_raw(handle as *mut NativeLru) });
}

/// Inserts `value` for `key`. Evicts the least important address first.
/// Returns `0` on success, `-1` on a null handle or a nonempty null buffer.
///
/// # Safety
/// `handle` from [`hc_lru_create`]. `key` / `value` are `key_len` /
/// `value_len` bytes when those lengths are nonzero.
#[no_mangle]
pub unsafe extern "C" fn hc_lru_put(
    handle: *mut c_void,
    key: *const u8,
    key_len: usize,
    value: *const u8,
    value_len: usize,
) -> i32 {
    let Some(native) = (unsafe { (handle as *mut NativeLru).as_mut() }) else {
        return -1;
    };
    if key_len > 0 && key.is_null() {
        return -1;
    }
    if value_len > 0 && value.is_null() {
        return -1;
    }
    let key = if key_len == 0 {
        Vec::new()
    } else {
        unsafe { std::slice::from_raw_parts(key, key_len) }.to_vec()
    };
    let value = if value_len == 0 {
        Vec::new()
    } else {
        unsafe { std::slice::from_raw_parts(value, value_len) }.to_vec()
    };
    native.cache.lock().unwrap_or_else(|err| err.into_inner()).put(key, value);
    0
}

/// Copies the value for `key`. `0` hit, `1` miss, `-1` bad args, `-3`
/// destination too small (`*written` is the needed length).
///
/// # Safety
/// Same as [`hc_lru_put`]. `written` may be null.
#[no_mangle]
pub unsafe extern "C" fn hc_lru_get(
    handle: *mut c_void,
    key: *const u8,
    key_len: usize,
    dest: *mut u8,
    dest_len: usize,
    written: *mut usize,
) -> i32 {
    let Some(native) = (unsafe { (handle as *mut NativeLru).as_mut() }) else {
        return -1;
    };
    if key_len > 0 && key.is_null() {
        return -1;
    }
    let key = if key_len == 0 {
        &[][..]
    } else {
        unsafe { std::slice::from_raw_parts(key, key_len) }
    };
    let mut guard = native.cache.lock().unwrap_or_else(|err| err.into_inner());
    let Some(value) = guard.get(key) else {
        return 1;
    };
    if !written.is_null() {
        unsafe { *written = value.len() };
    }
    if dest_len < value.len() {
        return -3;
    }
    if value.is_empty() {
        return 0;
    }
    if dest.is_null() {
        return -1;
    }
    unsafe { std::ptr::copy_nonoverlapping(value.as_ptr(), dest, value.len()) };
    0
}

/// # Safety
/// `handle` from [`hc_lru_create`] or null.
#[no_mangle]
pub unsafe extern "C" fn hc_lru_clear(handle: *mut c_void) {
    let Some(native) = (unsafe { (handle as *mut NativeLru).as_mut() }) else {
        return;
    };
    native.cache.lock().unwrap_or_else(|err| err.into_inner()).clear();
}

/// UTF-8 text, or `""` when `len` is 0 (never `from_raw_parts` on a null empty).
///
/// # Safety
/// `pointer` must be `len` bytes when `len` is nonzero.
unsafe fn utf8_or_empty<'a>(pointer: *const u8, len: usize) -> Result<&'a str, i32> {
    if len == 0 {
        return Ok("");
    }
    if pointer.is_null() {
        return Err(-1);
    }
    let bytes = unsafe { std::slice::from_raw_parts(pointer, len) };
    std::str::from_utf8(bytes).map_err(|_| -2)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn embed_text_empty_succeeds() {
        let mut output = [0.0_f32; STRUCTURAL_FEATURE_COUNT];
        let status = unsafe {
            hc_embed_text(std::ptr::null(), 0, output.as_mut_ptr(), output.len())
        };
        assert_eq!(status, 0);
        assert!(output.iter().all(|slot| *slot == 0.0));
    }

    #[test]
    fn cosine_identical_unit_is_one() {
        let left = [1.0_f32, 0.0];
        let right = [1.0_f32, 0.0];
        let score = unsafe { hc_cosine(left.as_ptr(), 2, right.as_ptr(), 2) };
        assert!((score - 1.0).abs() < 1e-12);
    }

    #[test]
    fn gemv_bias_matches_unit_source() {
        let weights = [1.0_f32, 3.0, 2.0, 4.0];
        let source = [1.0_f32, 0.0];
        let biases = [0.5_f32, -0.5];
        let mut destination = [0.0_f32; 2];
        let status = unsafe {
            hc_gemv_bias(
                weights.as_ptr(),
                2,
                2,
                source.as_ptr(),
                biases.as_ptr(),
                destination.as_mut_ptr(),
            )
        };
        assert_eq!(status, 0);
        assert_eq!(destination, [1.5, 2.5]);
    }

    #[test]
    fn support_cosine_clamps_opposite() {
        let left = [1.0_f32, 0.0];
        let right = [-1.0_f32, 0.0];
        let score = unsafe { hc_support_cosine(left.as_ptr(), 2, right.as_ptr(), 2) };
        assert_eq!(score, 0.0);
    }

    #[test]
    fn hash_embed_empty_succeeds() {
        let mut output = [1.0_f32; HASH_EMBED_BINS];
        let status = unsafe { hc_hash_embed(std::ptr::null(), 0, output.as_mut_ptr(), output.len()) };
        assert_eq!(status, 0);
        assert!(output.iter().all(|slot| *slot == 0.0));
    }

    #[test]
    fn batch_cosine_json_empty_docs_succeeds() {
        let query = [1.0_f32];
        let status = unsafe {
            hc_batch_cosine_json(query.as_ptr(), 1, std::ptr::null(), 0, std::ptr::null(), 0, std::ptr::null_mut())
        };
        assert_eq!(status, 0);
    }

    #[test]
    fn heap_should_spill_at_watermark() {
        assert_eq!(hc_heap_should_spill(70, 70, 1, 100), 1);
        assert_eq!(hc_heap_should_spill(69, 70, 1, 100), 0);
        assert_eq!(hc_heap_should_spill(10, 0, 70, 100), 1);
        assert_eq!(hc_heap_should_spill(-1, 10, 0, 0), -1);
    }

    #[test]
    fn lru_client_walk_d_on_a_b_c_is_d_a_c() {
        let cache = hc_lru_create(3);
        let put = |key: u8, value: u8| unsafe {
            hc_lru_put(cache, &key, 1, &value, 1)
        };
        let get = |key: u8| -> Option<u8> {
            let mut dest = [0_u8; 1];
            let mut written = 0_usize;
            let status = unsafe { hc_lru_get(cache, &key, 1, dest.as_mut_ptr(), 1, &mut written) };
            if status == 0 {
                Some(dest[0])
            } else {
                None
            }
        };

        assert_eq!(put(b'A', 1), 0);
        assert_eq!(put(b'B', 2), 0);
        assert_eq!(put(b'C', 3), 0);
        assert_eq!(get(b'A'), Some(1));
        assert_eq!(put(b'D', 4), 0);
        assert_eq!(get(b'D'), Some(4));
        assert_eq!(get(b'A'), Some(1));
        assert_eq!(get(b'C'), Some(3));
        assert_eq!(get(b'B'), None);
        unsafe { hc_lru_free(cache) };
    }

    #[test]
    fn heap_top_k_abs_keeps_largest() {
        let values = [0.1_f32, -2.0, 0.0, 3.0, 1e-8];
        let indexes = [10, 11, 12, 13, 14];
        let mut out_indexes = [0_i32; 2];
        let mut out_values = [0.0_f32; 2];
        let written = unsafe {
            hc_heap_top_k_abs(
                values.as_ptr(),
                indexes.as_ptr(),
                values.len(),
                2,
                out_indexes.as_mut_ptr(),
                out_values.as_mut_ptr(),
            )
        };
        assert_eq!(written, 2);
        assert_eq!(out_indexes, [13, 11]);
        assert!((out_values[0] - 3.0).abs() < 1e-6);
        assert!((out_values[1] - 2.0).abs() < 1e-6);
    }
}
