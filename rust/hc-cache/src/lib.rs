//! Pure-Rust cache helpers. **Not** a C ABI and **not** loaded by
//! `RustKernels.cs`. Core chat, EF, SignalR, SPA, and training stay
//! in C# / TypeScript.
//!
//! Eviction is **never FIFO**. FIFO drops the oldest insert even after
//! it was used again. The Client walk is `D >> [A,B,C] -> [D,A,C]`:
//! delete the **least important** address, then add the new one at
//! the left. After A is reused, B is least important — not A (FIFO)
//! and not a corrupt `[D, B, B]`.
//!
//! Other options (still not FIFO):
//! - [`LruCache`] — HashMap + doubly linked list, O(1)
//! - [`LfuCache`] — frequency + LRU tie-break
//! - [`ClockCache`] — second-chance ring

pub mod clock;
pub mod lfu;
pub mod locality;
pub mod lru;

pub use clock::ClockCache;
pub use lfu::LfuCache;
pub use locality::{hit_counts, PolicyHits};
pub use lru::LruCache;

#[cfg(test)]
mod tests {
    use super::*;

    /// Client reminder: `D >> [A,B,C] -> [D,A,C]` (not FIFO, not
    /// `[D,B,B]`). Resident set is A, B, C. A is reused so it is
    /// no longer the least-important insert. Delete the least
    /// important address (B), then add D at the left (most recent).
    #[test]
    fn client_d_shift_a_b_c_is_d_a_c() {
        let mut cache = LruCache::new(3);
        cache.put('A', 1);
        cache.put('B', 2);
        cache.put('C', 3);
        assert_eq!(cache.get(&'A'), Some(&1));
        cache.put('D', 4);
        assert_eq!(cache.keys_mru_to_lru(), vec!['D', 'A', 'C']);
        assert!(cache.get(&'B').is_none());
        assert_eq!(cache.get(&'A'), Some(&1));
        assert_eq!(cache.get(&'C'), Some(&3));
        assert_ne!(cache.keys_mru_to_lru(), vec!['D', 'B', 'B']);
        assert_ne!(cache.keys_mru_to_lru(), vec!['D', 'C', 'B']);
    }

    /// Same walk from an already-hot A: `[A, C, B]` + D → `[D, A, C]`.
    #[test]
    fn d_on_a_c_b_is_d_a_c() {
        let mut cache = LruCache::new(3);
        cache.put('B', 2);
        cache.put('C', 3);
        cache.put('A', 1);
        assert_eq!(cache.keys_mru_to_lru(), vec!['A', 'C', 'B']);
        cache.put('D', 4);
        assert_eq!(cache.keys_mru_to_lru(), vec!['D', 'A', 'C']);
        assert!(cache.get(&'B').is_none());
    }

    #[test]
    fn lfu_keeps_hot_key_over_one_shot() {
        let mut cache = LfuCache::new(2);
        cache.put('A', 1);
        cache.put('B', 2);
        assert_eq!(cache.get(&'A'), Some(&1));
        assert_eq!(cache.get(&'A'), Some(&1));
        cache.put('C', 3);
        assert_eq!(cache.get(&'A'), Some(&1));
        assert!(cache.get(&'B').is_none());
    }

    #[test]
    fn clock_second_chance_skips_referenced() {
        let mut cache = ClockCache::new(2);
        cache.put('A', 1);
        cache.put('B', 2);
        assert_eq!(cache.get(&'A'), Some(&1));
        cache.put('C', 3);
        assert_eq!(cache.get(&'A'), Some(&1));
        assert_eq!(cache.get(&'C'), Some(&3));
    }

    #[test]
    fn temporal_locality_trace_lru_keeps_working_set() {
        let keys = ['A', 'B', 'A', 'C', 'A', 'B', 'A'];
        let hits = hit_counts(2, &keys);
        assert!(hits.lru >= 3, "lru={}", hits.lru);
        assert_eq!(hits.accesses, 7);
    }

    #[test]
    fn lru_zero_capacity_is_empty() {
        let mut cache = LruCache::new(0);
        cache.put('A', 1);
        assert!(cache.is_empty());
        assert!(cache.get(&'A').is_none());
    }
}
