//! Column-major GEMV used by the in-process hashed MLP.
//!
//! Weight storage is Math.NET column-major (`rows` = targets, `cols` = sources):
//! column `c` occupies `weights[c * rows .. c * rows + rows]`. Silent training
//! may use LibTorch; this path is the IEEE-754 fallback `Forward` / backprop
//! still walk when traces are captured.

/// `destination = W source + biases` with a zero-skip on `source`.
pub fn multiply_bias(
    weights: &[f32],
    rows: usize,
    cols: usize,
    source: &[f32],
    biases: &[f32],
    destination: &mut [f32],
) -> bool {
    if !valid_gemv_shapes(weights, rows, cols, source.len(), biases.len(), destination.len()) {
        return false;
    }

    destination[..rows].copy_from_slice(&biases[..rows]);
    for column in 0..cols {
        let source_value = source[column];
        if source_value == 0.0 {
            continue;
        }
        let offset = column * rows;
        let weight_col = &weights[offset..offset + rows];
        for (dest, &weight) in destination.iter_mut().zip(weight_col.iter()) {
            *dest += weight * source_value;
        }
    }
    true
}

/// `destination = Wᵀ delta` (one sum per source column).
pub fn multiply_transpose(
    weights: &[f32],
    rows: usize,
    cols: usize,
    delta: &[f32],
    destination: &mut [f32],
) -> bool {
    let weight_count = match rows.checked_mul(cols) {
        Some(count) => count,
        None => return false,
    };
    if rows == 0
        || cols == 0
        || weights.len() < weight_count
        || delta.len() < rows
        || destination.len() < cols
    {
        return false;
    }

    for column in 0..cols {
        let offset = column * rows;
        let weight_col = &weights[offset..offset + rows];
        let mut sum = 0.0_f32;
        for (&weight, &delta_value) in weight_col.iter().zip(delta.iter()) {
            sum += weight * delta_value;
        }
        destination[column] = sum;
    }
    true
}

fn valid_gemv_shapes(
    weights: &[f32],
    rows: usize,
    cols: usize,
    source_len: usize,
    bias_len: usize,
    destination_len: usize,
) -> bool {
    let Some(weight_count) = rows.checked_mul(cols) else {
        return false;
    };
    rows > 0
        && cols > 0
        && weights.len() >= weight_count
        && source_len >= cols
        && bias_len >= rows
        && destination_len >= rows
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn multiply_bias_skips_zero_sources() {
        // W = [[1, 2], [3, 4]] column-major: [1, 3, 2, 4], x = [1, 0], b = [0.5, -0.5]
        let weights = [1.0_f32, 3.0, 2.0, 4.0];
        let source = [1.0_f32, 0.0];
        let biases = [0.5_f32, -0.5];
        let mut destination = [0.0_f32; 2];
        assert!(multiply_bias(&weights, 2, 2, &source, &biases, &mut destination));
        assert_eq!(destination, [1.5, 2.5]);
    }

    #[test]
    fn multiply_transpose_matches_column_sums() {
        let weights = [1.0_f32, 3.0, 2.0, 4.0];
        let delta = [1.0_f32, 1.0];
        let mut destination = [0.0_f32; 2];
        assert!(multiply_transpose(&weights, 2, 2, &delta, &mut destination));
        assert_eq!(destination, [4.0, 6.0]);
    }

    #[test]
    fn rejects_overflowing_shape() {
        let weights = [1.0_f32];
        let source = [1.0_f32];
        let biases = [0.0_f32];
        let mut destination = [0.0_f32];
        assert!(!multiply_bias(
            &weights,
            usize::MAX,
            2,
            &source,
            &biases,
            &mut destination
        ));
    }
}
