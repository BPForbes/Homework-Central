//! Pure-Rust cache and locality helpers for Homework Central.
//!
//! This crate is **not** a C ABI and is **not** loaded by `RustKernels.cs`.
//! Core chat, EF, SignalR, SPA, and training stay in C# / TypeScript.
//!
//! Policies (pick by workload; LRU is one option, not the only one):
//! - [`LruCache`] — recency / temporal locality (HashMap + doubly linked list)
//! - [`FifoCache`] — insertion order (ignores later uses)
//! - [`LfuCache`] — frequency (+ LRU tie-break)
//! - [`ClockCache`] — second-chance ring
//!
//! [`hit_counts`] replays a key trace so policies can be compared.

pub mod clock;
pub mod fifo;
pub mod lfu;
pub mod locality;
pub mod lru;

pub use clock::ClockCache;
pub use fifo::FifoCache;
pub use lfu::LfuCache;
pub use locality::{hit_counts, PolicyHits};
pub use lru::LruCache;

#[cfg(test)]
mod tests {
    use super::*;

    /// Client walk: three slots, left = most recent.
    /// Start [C, B, A], use A → [A, C, B], insert D → [D, A, C].
    #[test]
    fn lru_client_walk_promotes_then_drops_rightmost() {
        let mut cache = LruCache::new(3);
        cache.put('A', 1);
        cache.put('B', 2);
        cache.put('C', 3);
        assert_eq!(cache.keys_mru_to_lru(), vec!['C', 'B', 'A']);

        assert_eq!(cache.get(&'A'), Some(&1));
        assert_eq!(cache.keys_mru_to_lru(), vec!['A', 'C', 'B']);

        cache.put('D', 4);
        assert_eq!(cache.keys_mru_to_lru(), vec!['D', 'A', 'C']);
        assert!(cache.get(&'B').is_none());
        assert_eq!(cache.get(&'A'), Some(&1));
    }

    #[test]
    fn fifo_from_same_state_evicts_a_which_is_wrong_for_this_walk() {
        let mut cache = FifoCache::new(3);
        cache.put('A', 1);
        cache.put('B', 2);
        cache.put('C', 3);
        // FIFO does not promote A.
        assert_eq!(cache.get(&'A'), Some(&1));
        cache.put('D', 4);
        assert_eq!(cache.keys_newest_to_oldest(), vec!['D', 'C', 'B']);
        assert!(cache.get(&'A').is_none());
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
    fn temporal_locality_trace_lru_beats_fifo() {
        // Working set {A,B} with a one-shot C: LRU keeps A,B; FIFO drops A.
        let keys = ['A', 'B', 'A', 'C', 'A', 'B', 'A'];
        let hits = hit_counts(2, &keys);
        assert!(hits.lru > hits.fifo, "lru={} fifo={}", hits.lru, hits.fifo);
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
