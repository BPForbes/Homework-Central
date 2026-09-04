//! C ABI for the chat-monitor kernels the API loads at runtime.
//!
//! Only the lexical bins and store cosine cross this boundary. Encode
//! metadata, the hashed MLP, and the rest of the API stay in C#. The
//! Docker image does not ship `rustc`; C# falls back when this library
//! is absent.

use hc_feature_encode::{add_weighted_tokens, embed_text, STRUCTURAL_FEATURE_COUNT};
use hc_vector_cosine::cosine;

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
}
