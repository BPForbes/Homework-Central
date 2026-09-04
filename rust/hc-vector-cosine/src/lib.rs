//! Cosine similarity used by `VectorDocumentStore` retrieval.
//!
//! The C# store still loads candidate rows with EF. This crate scores
//! already-fetched embeddings so a later bind can replace the in-process
//! loop without changing persisted JSON float arrays.

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
}
