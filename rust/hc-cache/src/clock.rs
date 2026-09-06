//! Clock / second-chance: FIFO ring plus a referenced bit.
//!
//! Cheaper than true LRU when the working set is large; still respects
//! a recent use (the bit) instead of blindly dropping the oldest insert.

use std::collections::HashMap;
use std::hash::Hash;

struct Slot<K, V> {
    key: K,
    value: V,
    referenced: bool,
}

pub struct ClockCache<K, V> {
    cap: usize,
    map: HashMap<K, usize>,
    slots: Vec<Option<Slot<K, V>>>,
    hand: usize,
}

impl<K: Eq + Hash + Clone, V> ClockCache<K, V> {
    pub fn new(capacity: usize) -> Self {
        Self {
            cap: capacity,
            map: HashMap::new(),
            slots: (0..capacity).map(|_| None).collect(),
            hand: 0,
        }
    }

    pub fn len(&self) -> usize {
        self.map.len()
    }

    pub fn get(&mut self, key: &K) -> Option<&V> {
        let index = *self.map.get(key)?;
        if let Some(slot) = self.slots[index].as_mut() {
            slot.referenced = true;
        }
        self.slots[index].as_ref().map(|slot| &slot.value)
    }

    pub fn put(&mut self, key: K, value: V) {
        if self.cap == 0 {
            return;
        }
        if let Some(&index) = self.map.get(&key) {
            if let Some(slot) = self.slots[index].as_mut() {
                slot.value = value;
                slot.referenced = true;
            }
            return;
        }
        if self.map.len() == self.cap {
            self.evict_one();
        }
        let index = self
            .slots
            .iter()
            .position(Option::is_none)
            .expect("clock has a free slot after evict");
        self.map.insert(key.clone(), index);
        // New keys start unreferenced so a later get() is what grants
        // the second chance. Insert-only keys are first to leave.
        self.slots[index] = Some(Slot {
            key,
            value,
            referenced: false,
        });
    }

    fn evict_one(&mut self) {
        if self.cap == 0 {
            return;
        }
        loop {
            let index = self.hand;
            self.hand = (self.hand + 1) % self.cap;
            match self.slots[index].as_mut() {
                None => return,
                Some(slot) if slot.referenced => slot.referenced = false,
                Some(slot) => {
                    let key = slot.key.clone();
                    self.slots[index] = None;
                    self.map.remove(&key);
                    return;
                }
            }
        }
    }
}
