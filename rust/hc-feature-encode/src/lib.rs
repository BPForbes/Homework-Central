//! Lexical feature bins for chat-monitor retrieval vectors.
//!
//! This crate matches `ChatMonitoringFeatureEncoder.EmbedText` in
//! `backend/HomeworkCentral.Api/Assessment/ChatMonitoringFeatureEncoder.cs`.
//! The structural width stays 86 floats so persisted `VectorDocument`
//! JSON arrays remain cosine-comparable.
//!
//! Tokenization and FNV-1a hashing walk UTF-16 code units after invariant
//! lowercasing so the bins match the C# `char` loop. The API calls this
//! through `hc-kernels` when `libhc_kernels` is present.

pub const STRUCTURAL_FEATURE_COUNT: usize = 86;
pub const HASH_BIN_COUNT: usize = 44;
pub const TOKEN_LIMIT: usize = 400;
pub const HASH_EMBED_BINS: usize = 64;

const FNV_OFFSET: u32 = 2_166_136_261;
const FNV_PRIME: u32 = 16_777_619;

const SEPARATORS: &[char] = &[
    ' ', '\r', '\n', '\t', '.', ',', '!', '?', ':', ';', '"', '\'', '(', ')',
    '[', ']', '{', '}', '/', '\\', '-', '_',
];

/// Hashed unigram/bigram bins for `text` at weight `1.0`.
/// Remaining structural slots stay zero, matching `EmbedText`.
pub fn embed_text(text: &str) -> [f32; STRUCTURAL_FEATURE_COUNT] {
    let mut values = [0.0_f32; STRUCTURAL_FEATURE_COUNT];
    add_weighted_tokens(&mut values, text, 1.0);
    values
}

/// Writes hashed unigram/bigram bins into `values` (same as C# `AddTokens`).
pub fn add_weighted_tokens(values: &mut [f32], text: &str, weight: f32) {
    let tokens = tokenize(text);
    let mut previous = String::new();
    for token in tokens.into_iter().take(TOKEN_LIMIT) {
        add_feature(values, &token, weight);
        if !previous.is_empty() {
            let mut bigram = String::with_capacity(previous.len() + 1 + token.len());
            bigram.push_str(&previous);
            bigram.push('_');
            bigram.push_str(&token);
            add_feature(values, &bigram, weight * 0.7);
        }
        previous = token;
    }
}

fn tokenize(text: &str) -> Vec<String> {
    let lowered = text.to_lowercase();
    let mut tokens = Vec::new();
    let mut current = String::new();
    for character in lowered.chars() {
        if SEPARATORS.contains(&character) {
            flush_token(&mut current, &mut tokens);
            continue;
        }
        current.push(character);
    }
    flush_token(&mut current, &mut tokens);
    tokens
}

fn flush_token(current: &mut String, tokens: &mut Vec<String>) {
    let trimmed = current.trim();
    if !trimmed.is_empty() {
        tokens.push(trimmed.to_string());
    }
    current.clear();
}

/// Offline Ollama fallback: 64-bin UTF-16 histogram, then L2 normalize.
/// Matches `LlmClient.HashEmbed` (`char` walk, float sum-of-squares, double divide).
pub fn hash_embed(text: &str) -> [f32; HASH_EMBED_BINS] {
    let mut vector = [0.0_f32; HASH_EMBED_BINS];
    for unit in text.encode_utf16() {
        let index = (unit as usize) % HASH_EMBED_BINS;
        vector[index] += 1.0;
    }

    let sum_squares: f32 = vector.iter().map(|slot| slot * slot).sum();
    let norm = f64::from(sum_squares).sqrt();
    if norm > 0.0 {
        for slot in &mut vector {
            *slot = (*slot as f64 / norm) as f32;
        }
    }
    vector
}

/// Tutoring stage-1 expertise slot. Walks UTF-16 after trim + lowercase, then
/// saturates the hashed bin at 1 so many labels cannot drown the 32-wide region.
pub fn add_expertise_hash(
    values: &mut [f32],
    label: &str,
    base_input_size: usize,
    bin_count: usize,
) -> bool {
    if bin_count == 0 {
        return false;
    }
    let Some(required) = base_input_size.checked_add(bin_count) else {
        return false;
    };
    if values.len() < required {
        return false;
    }

    let key = label.trim().to_lowercase();
    if key.is_empty() {
        return true;
    }

    let mut hash = FNV_OFFSET;
    for unit in key.encode_utf16() {
        hash ^= u32::from(unit);
        hash = hash.wrapping_mul(FNV_PRIME);
    }
    let index = base_input_size + (hash as usize % bin_count);
    values[index] = (values[index] + 1.0).clamp(0.0, 1.0);
    true
}

fn add_feature(values: &mut [f32], value: &str, weight: f32) {
    let mut hash = FNV_OFFSET;
    for unit in value.encode_utf16() {
        hash ^= u32::from(unit);
        hash = hash.wrapping_mul(FNV_PRIME);
    }
    let index = (hash % HASH_BIN_COUNT as u32) as usize;
    values[index] = (values[index] + weight).clamp(-4.0, 4.0);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_text_is_all_zeros() {
        assert_eq!(embed_text(""), [0.0; STRUCTURAL_FEATURE_COUNT]);
    }

    #[test]
    fn anything_matches_csharp_embed_text() {
        let values = embed_text("anything");
        assert_eq!(values.len(), STRUCTURAL_FEATURE_COUNT);
        assert_eq!(values[15], 1.0);
        assert_eq!(values.iter().filter(|slot| **slot != 0.0).count(), 1);
    }

    #[test]
    fn payment_please_matches_csharp_embed_text() {
        let values = embed_text("payment please");
        assert_eq!(values[13], 1.0);
        assert_eq!(values[19], 1.0);
        assert_eq!(values[28], 0.7);
        assert_eq!(values.iter().filter(|slot| **slot != 0.0).count(), 3);
    }

    #[test]
    fn punctuation_and_case_match_csharp_split() {
        let values = embed_text("Hello, WORLD!! payment-please");
        assert_eq!(values[2], 0.7);
        assert_eq!(values[11], 1.0);
        assert_eq!(values[13], 1.0);
        assert_eq!(values[16], 0.7);
        assert_eq!(values[19], 1.0);
        assert_eq!(values[28], 0.7);
        assert_eq!(values[39], 1.0);
        assert_eq!(values.iter().filter(|slot| **slot != 0.0).count(), 7);
    }

    #[test]
    fn token_limit_clamps_repeated_unigrams() {
        let long = "a ".repeat(500);
        let values = embed_text(&long);
        assert_eq!(values[20], 4.0);
        assert_eq!(values[40], 4.0);
        assert!(values.iter().all(|slot| *slot <= 4.0 && *slot >= -4.0));
    }

    #[test]
    fn hash_embed_empty_is_zeros() {
        assert_eq!(hash_embed(""), [0.0; HASH_EMBED_BINS]);
    }

    #[test]
    fn hash_embed_offline_is_unit_length() {
        let values = hash_embed("offline");
        let norm: f64 = values.iter().map(|slot| f64::from(*slot * *slot)).sum::<f64>().sqrt();
        assert!((norm - 1.0).abs() < 1e-6);
        assert_eq!(values.len(), HASH_EMBED_BINS);
    }

    #[test]
    fn expertise_hash_is_idempotent_at_one() {
        let mut values = [0.0_f32; 62];
        assert!(add_expertise_hash(&mut values, "  Rust  ", 30, 32));
        let filled = values.iter().skip(30).filter(|slot| **slot != 0.0).count();
        assert_eq!(filled, 1);
        let first = values;
        assert!(add_expertise_hash(&mut values, "rust", 30, 32));
        assert_eq!(values, first);
    }

    #[test]
    fn expertise_whitespace_is_noop() {
        let mut values = [0.0_f32; 62];
        assert!(add_expertise_hash(&mut values, "   ", 30, 32));
        assert!(values.iter().all(|slot| *slot == 0.0));
    }
}
