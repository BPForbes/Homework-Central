//! FIFO: evicts in insertion order. Ignores later uses (no temporal locality).

use std::collections::{HashMap, VecDeque};
use std::hash::Hash;

pub struct FifoCache<K, V> {
    cap: usize,
    map: HashMap<K, V>,
    order: VecDeque<K>,
}

impl<K: Eq + Hash + Clone, V> FifoCache<K, V> {
    pub fn new(capacity: usize) -> Self {
        Self {
            cap: capacity,
            map: HashMap::new(),
            order: VecDeque::new(),
        }
    }

    pub fn len(&self) -> usize {
        self.map.len()
    }

    pub fn get(&self, key: &K) -> Option<&V> {
        self.map.get(key)
    }

    pub fn put(&mut self, key: K, value: V) {
        if self.cap == 0 {
            return;
        }
        if self.map.contains_key(&key) {
            self.map.insert(key, value);
            return;
        }
        if self.map.len() == self.cap {
            if let Some(oldest) = self.order.pop_front() {
                self.map.remove(&oldest);
            }
        }
        self.order.push_back(key.clone());
        self.map.insert(key, value);
    }

    /// Left = newest insert, right = oldest insert (same display as LRU).
    pub fn keys_newest_to_oldest(&self) -> Vec<K> {
        self.order.iter().rev().cloned().collect()
    }
}
