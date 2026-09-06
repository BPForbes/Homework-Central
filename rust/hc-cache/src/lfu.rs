//! LFU: evict the least-frequently used key. Tie-break is LRU (oldest use).
//!
//! Frequency is the other locality signal: a key used many times stays even
//! if it was not the most recent single access.

use std::collections::HashMap;
use std::hash::Hash;

struct Entry<V> {
    value: V,
    freq: u64,
    last_use: u64,
}

pub struct LfuCache<K, V> {
    cap: usize,
    map: HashMap<K, Entry<V>>,
    clock: u64,
}

impl<K: Eq + Hash + Clone, V> LfuCache<K, V> {
    pub fn new(capacity: usize) -> Self {
        Self {
            cap: capacity,
            map: HashMap::new(),
            clock: 0,
        }
    }

    pub fn len(&self) -> usize {
        self.map.len()
    }

    pub fn get(&mut self, key: &K) -> Option<&V> {
        let clock = self.tick();
        let entry = self.map.get_mut(key)?;
        entry.freq = entry.freq.saturating_add(1);
        entry.last_use = clock;
        Some(&entry.value)
    }

    pub fn put(&mut self, key: K, value: V) {
        if self.cap == 0 {
            return;
        }
        if self.map.contains_key(&key) {
            let clock = self.tick();
            if let Some(entry) = self.map.get_mut(&key) {
                entry.value = value;
                entry.freq = entry.freq.saturating_add(1);
                entry.last_use = clock;
            }
            return;
        }
        if self.map.len() == self.cap {
            self.evict();
        }
        let clock = self.tick();
        self.map.insert(
            key,
            Entry {
                value,
                freq: 1,
                last_use: clock,
            },
        );
    }

    fn evict(&mut self) {
        let victim = self
            .map
            .iter()
            .min_by(|left, right| {
                left.1
                    .freq
                    .cmp(&right.1.freq)
                    .then(left.1.last_use.cmp(&right.1.last_use))
            })
            .map(|(key, _)| key.clone());
        if let Some(key) = victim {
            self.map.remove(&key);
        }
    }

    fn tick(&mut self) -> u64 {
        self.clock = self.clock.saturating_add(1);
        self.clock
    }
}
