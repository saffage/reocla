use crate::error::*;

#[derive(Debug, Clone)]
pub struct Stack<T>(Vec<T>);

impl<T> Default for Stack<T> {
    fn default() -> Self {
        Self(Default::default())
    }
}

impl<T> Stack<T> {
    pub fn push(&mut self, o: T) {
        self.0.push(o);
    }

    pub fn pop(&mut self) -> Result<T> {
        check_boundaries(self.0.pop())
    }

    pub fn peek(&self) -> Result<&T> {
        check_boundaries(self.0.last())
    }

    pub fn peek_mut(&mut self) -> Result<&mut T> {
        check_boundaries(self.0.last_mut())
    }

    pub fn len(&self) -> usize {
        self.0.len()
    }

    pub fn into_inner(self) -> Vec<T> {
        self.0
    }
}

fn check_boundaries<T>(x: Option<T>) -> Result<T> {
    x.ok_or(error!("Out of stack"))
}
