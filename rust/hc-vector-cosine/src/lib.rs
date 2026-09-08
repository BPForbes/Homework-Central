//! Cosine similarity used by `VectorDocumentStore` retrieval.
//!
//! The C# store still loads candidate rows with EF. Cosine of already-fetched
//! embeddings runs here through `hc-kernels` when `libhc_kernels` is present.

#[derive(Clone, Debug, PartialEq)]
pub struct RankedDocument<Id> {
    pub id: Id,
    pub score: f64,
}

/// Cosine of the overlapping prefix. Empty or zero-norm vectors score `0`,
/// matching `VectorDocumentStore.Cosine`.
pub fn cosine(left: &[f32], right: &[f32]) -> f64 {
    let count = left.len().min(right.len());
    if count == 0 {
        return 0.0;
    }

    let mut dot = 0.0_f64;
    let mut left_norm = 0.0_f64;
    let mut right_norm = 0.0_f64;
    for index in 0..count {
        // C# `VectorDocumentStore.Cosine` multiplies `float` lanes, then adds into `double`.
        dot += f64::from(left[index] * right[index]);
        left_norm += f64::from(left[index] * left[index]);
        right_norm += f64::from(right[index] * right[index]);
    }

    let denom = left_norm.sqrt() * right_norm.sqrt();
    if denom <= 0.0 {
        0.0
    } else {
        dot / denom
    }
}

/// Support-set cosine for the hashed MLP. Same float-then-widen product as
/// store cosine, but the score is clamped to `[0, 1]` and a non-positive
/// norm on either side is `0` (matches `ChatMonitoringNeuralModelHashedMlp.Cosine`).
pub fn support_cosine(left: &[f32], right: &[f32]) -> f64 {
    let count = left.len().min(right.len());
    let mut dot = 0.0_f64;
    let mut left_norm = 0.0_f64;
    let mut right_norm = 0.0_f64;
    for index in 0..count {
        dot += f64::from(left[index] * right[index]);
        left_norm += f64::from(left[index] * left[index]);
        right_norm += f64::from(right[index] * right[index]);
    }

    if left_norm <= 0.0 || right_norm <= 0.0 {
        return 0.0;
    }

    (dot / (left_norm * right_norm).sqrt()).clamp(0.0, 1.0)
}

/// Largest support cosine against `query`. `packed` is the concatenation of
/// `lengths.len()` vectors (no zero-pad — widths may differ).
pub fn max_support_cosine(query: &[f32], packed: &[f32], lengths: &[usize]) -> Option<f64> {
    let expected = lengths.iter().try_fold(0usize, |total, length| total.checked_add(*length))?;
    if expected != packed.len() {
        return None;
    }

    let mut offset = 0usize;
    let mut best = 0.0_f64;
    for length in lengths {
        let end = offset + *length;
        let score = support_cosine(query, &packed[offset..end]);
        if score > best {
            best = score;
        }
        offset = end;
    }
    Some(best)
}

/// `System.Text.Json` float-array shape used for `VectorDocument.EmbeddingJson`.
/// Any parse failure returns empty so cosine scores `0`, matching `Parse` + `?? []`.
pub fn parse_f32_json_array(json: &str) -> Vec<f32> {
    let trimmed = json.trim();
    if !trimmed.starts_with('[') || !trimmed.ends_with(']') {
        return Vec::new();
    }

    let inner = trimmed[1..trimmed.len() - 1].trim();
    if inner.is_empty() {
        return Vec::new();
    }

    let mut values = Vec::new();
    for part in inner.split(',') {
        let token = part.trim();
        match token.parse::<f32>() {
            Ok(value) if value.is_finite() => values.push(value),
            _ => return Vec::new(),
        }
    }
    values
}

/// One store cosine per JSON embedding blob, in input order.
pub fn batch_cosine_json(query: &[f32], json_blobs: &[&str]) -> Vec<f64> {
    json_blobs
        .iter()
        .map(|json| cosine(query, &parse_f32_json_array(json)))
        .collect()
}

/// Highest `take` documents by cosine, stable for equal scores (original order).
pub fn top_k_cosine<Id: Clone>(
    query: &[f32],
    documents: &[(Id, &[f32])],
    take: usize,
) -> Vec<RankedDocument<Id>> {
    if take == 0 {
        return Vec::new();
    }

    let mut ranked: Vec<RankedDocument<Id>> = documents
        .iter()
        .map(|(id, embedding)| RankedDocument {
            id: id.clone(),
            score: cosine(query, embedding),
        })
        .collect();
    ranked.sort_by(|left, right| {
        right
            .score
            .partial_cmp(&left.score)
            .unwrap_or(std::cmp::Ordering::Equal)
    });
    ranked.truncate(take);
    ranked
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_vectors_score_zero() {
        assert_eq!(cosine(&[], &[1.0]), 0.0);
        assert_eq!(cosine(&[1.0], &[]), 0.0);
    }

    #[test]
    fn identical_unit_vectors_score_one() {
        let score = cosine(&[1.0, 0.0], &[1.0, 0.0]);
        assert!((score - 1.0).abs() < 1e-12);
    }

    #[test]
    fn orthogonal_vectors_score_zero() {
        assert!((cosine(&[1.0, 0.0], &[0.0, 1.0])).abs() < 1e-12);
    }

    #[test]
    fn zero_norm_scores_zero() {
        assert_eq!(cosine(&[0.0, 0.0], &[1.0, 2.0]), 0.0);
    }

    #[test]
    fn f32_product_then_widen_matches_csharp() {
        let left = [std::f32::consts::PI, std::f32::consts::E, 0.1];
        let right = [std::f32::consts::E, std::f32::consts::PI, 0.2];
        let mut dot = 0.0_f64;
        let mut left_norm = 0.0_f64;
        let mut right_norm = 0.0_f64;
        let mut widened_dot = 0.0_f64;
        for index in 0..left.len() {
            dot += f64::from(left[index] * right[index]);
            left_norm += f64::from(left[index] * left[index]);
            right_norm += f64::from(right[index] * right[index]);
            widened_dot += f64::from(left[index]) * f64::from(right[index]);
        }
        assert!((dot - widened_dot).abs() > 1e-10);
        let expected = dot / (left_norm.sqrt() * right_norm.sqrt());
        assert!((cosine(&left, &right) - expected).abs() < 1e-18);
    }

    #[test]
    fn overlapping_prefix_ignores_tail() {
        let score = cosine(&[1.0], &[1.0, 99.0]);
        assert!((score - 1.0).abs() < 1e-12);
    }

    #[test]
    fn top_k_orders_by_descending_cosine() {
        let query = [1.0_f32, 0.0];
        let a = [1.0_f32, 0.0];
        let b = [0.0_f32, 1.0];
        let c = [0.7_f32, 0.7];
        let ranked = top_k_cosine(&query, &[("a", a.as_slice()), ("b", b.as_slice()), ("c", c.as_slice())], 2);
        assert_eq!(ranked.len(), 2);
        assert_eq!(ranked[0].id, "a");
        assert_eq!(ranked[1].id, "c");
        assert!(ranked[0].score > ranked[1].score);
    }

    #[test]
    fn top_k_zero_take_is_empty() {
        let query = [1.0_f32];
        let doc = [1.0_f32];
        assert!(top_k_cosine(&query, &[("a", doc.as_slice())], 0).is_empty());
    }

    #[test]
    fn support_cosine_clamps_negative() {
        let left = [1.0_f32, 0.0];
        let right = [-1.0_f32, 0.0];
        assert_eq!(support_cosine(&left, &right), 0.0);
        assert!((cosine(&left, &right) + 1.0).abs() < 1e-12);
    }

    #[test]
    fn support_cosine_zero_norm_is_zero() {
        assert_eq!(support_cosine(&[0.0, 0.0], &[1.0, 2.0]), 0.0);
    }

    #[test]
    fn max_support_cosine_picks_the_best() {
        let query = [1.0_f32, 0.0];
        let packed = [0.0_f32, 1.0, 1.0, 0.0];
        let best = max_support_cosine(&query, &packed, &[2, 2]).unwrap();
        assert!((best - 1.0).abs() < 1e-12);
    }

    #[test]
    fn parse_f32_json_array_reads_compact_stj() {
        assert_eq!(parse_f32_json_array("[1,0,0.5]"), vec![1.0, 0.0, 0.5]);
        assert_eq!(parse_f32_json_array(" [ 1e-1 , 2 ] "), vec![0.1, 2.0]);
        assert!(parse_f32_json_array("[1,2,]").is_empty());
        assert!(parse_f32_json_array("null").is_empty());
        assert!(parse_f32_json_array("[]").is_empty());
    }

    #[test]
    fn batch_cosine_json_scores_in_order() {
        let query = [1.0_f32, 0.0];
        let scores = batch_cosine_json(&query, &["[1,0]", "[0,1]", "nope"]);
        assert!((scores[0] - 1.0).abs() < 1e-12);
        assert!(scores[1].abs() < 1e-12);
        assert_eq!(scores[2], 0.0);
    }
}
