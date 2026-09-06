//! LRU: HashMap + doubly linked list, O(1) get / put / evict.
//!
//! Order is **left = most recent, right = least recent**.
//! Nodes store `prev` / `next` indexes so unlink does not scan.

use std::borrow::Borrow;
use std::collections::HashMap;
use std::hash::Hash;

struct Node<K, V> {
    key: K,
    value: V,
    prev: Option<usize>,
    next: Option<usize>,
}

/// Least-recently-used cache. Exploits temporal locality: a just-used
/// key is moved to the left (MRU) so a one-shot insert is not evicted
/// ahead of a key that was used again.
pub struct LruCache<K, V> {
    cap: usize,
    map: HashMap<K, usize>,
    nodes: Vec<Node<K, V>>,
    free: Vec<usize>,
    head: Option<usize>,
    tail: Option<usize>,
}

impl<K: Eq + Hash + Clone, V> LruCache<K, V> {
    pub fn new(capacity: usize) -> Self {
        Self {
            cap: capacity,
            map: HashMap::new(),
            nodes: Vec::new(),
            free: Vec::new(),
            head: None,
            tail: None,
        }
    }

    pub fn capacity(&self) -> usize {
        self.cap
    }

    pub fn len(&self) -> usize {
        self.map.len()
    }

    pub fn is_empty(&self) -> bool {
        self.map.is_empty()
    }

    /// Keys from most recent (left) to least recent (right).
    pub fn keys_mru_to_lru(&self) -> Vec<K> {
        let mut out = Vec::with_capacity(self.map.len());
        let mut cursor = self.head;
        while let Some(index) = cursor {
            out.push(self.nodes[index].key.clone());
            cursor = self.nodes[index].next;
        }
        out
    }

    pub fn get<Q>(&mut self, key: &Q) -> Option<&V>
    where
        Q: Hash + Eq + ?Sized,
        K: Borrow<Q>,
    {
        let index = *self.map.get(key)?;
        self.promote(index);
        Some(&self.nodes[index].value)
    }

    pub fn clear(&mut self) {
        self.map.clear();
        self.free.clear();
        self.nodes.clear();
        self.head = None;
        self.tail = None;
    }

    pub fn put(&mut self, key: K, value: V) {
        if self.cap == 0 {
            return;
        }
        if let Some(&index) = self.map.get(&key) {
            self.nodes[index].value = value;
            self.promote(index);
            return;
        }
        if self.map.len() == self.cap {
            self.evict_lru();
        }
        let index = self.alloc(key.clone(), value);
        self.map.insert(key, index);
        self.attach_mru(index);
    }

    fn alloc(&mut self, key: K, value: V) -> usize {
        let node = Node {
            key,
            value,
            prev: None,
            next: None,
        };
        if let Some(index) = self.free.pop() {
            self.nodes[index] = node;
            index
        } else {
            self.nodes.push(node);
            self.nodes.len() - 1
        }
    }

    fn promote(&mut self, index: usize) {
        if self.head == Some(index) {
            return;
        }
        self.unlink(index);
        self.attach_mru(index);
    }

    fn evict_lru(&mut self) {
        let Some(index) = self.tail else {
            return;
        };
        let key = self.nodes[index].key.clone();
        self.unlink(index);
        self.map.remove(&key);
        self.free.push(index);
    }

    fn unlink(&mut self, index: usize) {
        let prev = self.nodes[index].prev;
        let next = self.nodes[index].next;
        if let Some(prev) = prev {
            self.nodes[prev].next = next;
        } else {
            self.head = next;
        }
        if let Some(next) = next {
            self.nodes[next].prev = prev;
        } else {
            self.tail = prev;
        }
        self.nodes[index].prev = None;
        self.nodes[index].next = None;
    }

    fn attach_mru(&mut self, index: usize) {
        self.nodes[index].prev = None;
        self.nodes[index].next = self.head;
        if let Some(old_head) = self.head {
            self.nodes[old_head].prev = Some(index);
        } else {
            self.tail = Some(index);
        }
        self.head = Some(index);
    }
}
