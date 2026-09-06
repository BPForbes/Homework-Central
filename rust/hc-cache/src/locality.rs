//! Replay a key trace under each policy. Used to compare hit rates
//! when temporal locality is present or absent.

use crate::{ClockCache, FifoCache, LfuCache, LruCache};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PolicyHits {
    pub lru: usize,
    pub fifo: usize,
    pub lfu: usize,
    pub clock: usize,
    pub accesses: usize,
}

/// Count hits for `keys` on caches of `capacity`. First sighting of a key
/// is a miss + insert; later sightings are hits if still resident.
pub fn hit_counts<K: Eq + std::hash::Hash + Clone>(capacity: usize, keys: &[K]) -> PolicyHits {
    let mut lru = LruCache::new(capacity);
    let mut fifo = FifoCache::new(capacity);
    let mut lfu = LfuCache::new(capacity);
    let mut clock = ClockCache::new(capacity);
    let mut hits = PolicyHits {
        lru: 0,
        fifo: 0,
        lfu: 0,
        clock: 0,
        accesses: keys.len(),
    };
    for key in keys {
        if lru.get(key).is_some() {
            hits.lru += 1;
        } else {
            lru.put(key.clone(), ());
        }
        if fifo.get(key).is_some() {
            hits.fifo += 1;
        } else {
            fifo.put(key.clone(), ());
        }
        if lfu.get(key).is_some() {
            hits.lfu += 1;
        } else {
            lfu.put(key.clone(), ());
        }
        if clock.get(key).is_some() {
            hits.clock += 1;
        } else {
            clock.put(key.clone(), ());
        }
    }
    hits
}
