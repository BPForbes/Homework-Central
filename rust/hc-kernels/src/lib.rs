//! C ABI for the chat-monitor kernels the API loads at runtime.
//!
//! Lexical bins, store cosine, GEMV, expertise hash, HashEmbed, JSON
//! batch cosine, and support-set cosine cross this boundary. Encode
//! metadata, hashed-MLP train/replay, and the rest of the API stay in C#.
//! The Docker image does not ship `rustc`; C# falls back when this
//! library is absent or a newer export is missing.

use hc_feature_encode::{
    add_expertise_hash, add_weighted_tokens, embed_text, hash_embed, HASH_EMBED_BINS,
    STRUCTURAL_FEATURE_COUNT,
};
use hc_gemv::{multiply_bias, multiply_transpose};
use hc_vector_cosine::{batch_cosine_json, cosine, max_support_cosine, support_cosine};

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
}
