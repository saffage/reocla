use std::cmp::Ordering;
use std::collections::HashMap;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::{env, fs, io, str};

mod error;
mod parser;
mod stack;

use error::*;
use parser::Object;
use stack::Stack;

type ProcFrame = HashMap<String, Object>;

enum Proc {
    Aocla { body: Object, frame: ProcFrame },
    Rust(fn(&mut AoclaCtx) -> Result),
}

#[derive(Default)]
struct AoclaCtx {
    filename: String,
    stack: Stack<Object>,
    handlers: HashMap<String, Object>,
    proc: HashMap<String, Proc>,
    frame: ProcFrame,
    cur_proc_name: Option<String>,
}

impl AoclaCtx {
    fn new() -> Result<Self> {
        let mut ctx = Self::default();
        ctx.load_library()?;
        Ok(ctx)
    }

    fn cur_proc_name(&self) -> Result<&str> {
        self.cur_proc_name
            .as_deref()
            .ok_or(error!("Not inside procedure"))
    }

    fn add_string_proc(&mut self, name: &str, body: &str) -> Result {
        let body = parser::parse_root(body).map_err(string_to_error)?;
        self.add_proc(
            name,
            Proc::Aocla {
                body,
                frame: ProcFrame::new(),
            },
        );
        Ok(())
    }

    fn add_rust_proc(&mut self, name: &str, f: fn(&mut Self) -> Result) {
        self.add_proc(name, Proc::Rust(f));
    }

    fn add_proc(&mut self, name: &str, proc: Proc) {
        self.proc.insert(name.to_owned(), proc);
    }

    fn load_library(&mut self) -> Result {
        self.add_rust_proc("+", proc_arithmetic);
        self.add_rust_proc("-", proc_arithmetic);
        self.add_rust_proc("*", proc_arithmetic);
        self.add_rust_proc("/", proc_arithmetic);
        self.add_rust_proc("=", proc_compare);
        self.add_rust_proc("<>", proc_compare);
        self.add_rust_proc(">=", proc_compare);
        self.add_rust_proc("<=", proc_compare);
        self.add_rust_proc(">", proc_compare);
        self.add_rust_proc("<", proc_compare);
        self.add_rust_proc("|", proc_concat);
        self.add_rust_proc("::", proc_cons);
        self.add_rust_proc("@", proc_get);
        self.add_rust_proc("->", proc_append);
        self.add_rust_proc("<-", proc_prepend);
        self.add_rust_proc("and", proc_boolean);
        self.add_rust_proc("or", proc_boolean);
        self.add_rust_proc("not", proc_boolean);
        self.add_rust_proc("print", print_proc);
        self.add_rust_proc("println", print_proc);
        self.add_rust_proc("proc", proc_proc);
        self.add_rust_proc("if", proc_if);
        self.add_rust_proc("if-else", proc_if);
        self.add_rust_proc("while", proc_while);
        self.add_rust_proc("len", proc_len);
        self.add_rust_proc("head", proc_head);
        self.add_rust_proc("tail", proc_tail);
        self.add_rust_proc("eval", proc_eval);
        self.add_rust_proc("catch", proc_catch);
        self.add_rust_proc("throw", proc_throw);
        self.add_rust_proc("match", proc_match);
        self.add_rust_proc("str", proc_str);
        self.add_rust_proc("int", proc_int);
        self.add_rust_proc("read-file", proc_read_file);
        self.add_rust_proc("read-lines", proc_read_lines);
        self.add_rust_proc("write-file", proc_write_file);
        self.add_rust_proc("write-lines", proc_write_lines);
        self.add_rust_proc("write-stack", proc_write_stack);
        self.add_rust_proc("import", proc_import);
        self.add_string_proc("inc", "1 +")?;
        self.add_string_proc("dec", "1 -")?;
        self.add_string_proc("dup", "(x) $x $x")?;
        self.add_string_proc("swap", "(x y) $y $x")?;
        self.add_string_proc("drop", "(_)")?;
        Ok(())
    }

    fn call_proc(
        &mut self,
        proc_name: String,
        f: impl Fn(&mut Self) -> Result,
    ) -> Result {
        let prev_proc_name = self.cur_proc_name.clone();

        self.cur_proc_name = Some(proc_name);
        f(self)?;
        self.cur_proc_name = prev_proc_name;

        Ok(())
    }

    fn call_aocla_proc(
        &mut self,
        proc_name: String,
        proc_body: Object,
        proc_frame: ProcFrame,
    ) -> Result {
        let prev_stack_frame = self.frame.clone();

        self.frame = proc_frame;
        self.call_proc(proc_name, |ctx| ctx.eval(&proc_body))?;
        self.frame = prev_stack_frame;

        Ok(())
    }

    fn dequote_and_push(&mut self, mut notq: Object) {
        match notq {
            Object::Tuple(_, ref mut is_quoted)
            | Object::Sym(_, ref mut is_quoted) => {
                *is_quoted = false;
            }
            _ => unreachable!(),
        }
        self.stack.push(notq);
    }

    fn eval_tuple(&mut self, tuple: &[Object]) -> Result {
        for obj in tuple.iter().rev() {
            let Object::Sym(sym, _) = &obj else {
                return Err(error!(
                    "Only objects of type Symbol can be captured"
                ));
            };
            let obj = self.stack.pop()?;
            self.frame.insert(sym.clone(), obj);
        }
        Ok(())
    }

    fn eval_symbol(&mut self, sym: &String) -> Result {
        if let Some(sym) = sym.strip_prefix('$') {
            let local = self
                .frame
                .get(sym)
                .ok_or(error!("Unbound local variable: `{}`", sym))?;
            self.stack.push(local.clone());
        } else {
            let proc = self
                .proc
                .get(sym)
                .ok_or(error!("Unbound procedure `{}`", sym))?;
            match proc {
                Proc::Rust(f) => self.call_proc(sym.clone(), *f)?,
                Proc::Aocla { body, frame } => self.call_aocla_proc(
                    sym.clone(),
                    body.clone(),
                    frame.clone(),
                )?,
            }
        }
        Ok(())
    }

    fn eval(&mut self, root_obj: &Object) -> Result {
        let Object::List(root_obj_list) = &root_obj else {
            bail!("Root object must be of type List");
        };

        for obj in root_obj_list {
            match &obj {
                Object::Tuple(tuple, is_quoted) => {
                    if *is_quoted {
                        self.dequote_and_push(obj.clone());
                    } else {
                        if self.stack.len() < tuple.len() {
                            bail!(
                                "Out of stack while capturing local variable",
                            );
                        }
                        self.eval_tuple(tuple)?;
                    }
                }
                Object::Sym(sym, is_quoted) => {
                    if *is_quoted {
                        self.dequote_and_push(obj.clone());
                    } else {
                        self.eval_symbol(sym)?;
                    }
                }
                _ => self.stack.push(obj.clone()),
            }
        }
        Ok(())
    }

    fn throw(&mut self, tag: &str) -> std::result::Result<(), AoclaError> {
        match self.handlers.get(tag) {
            Some(handler_block) => self.eval(&handler_block.clone()),
            None => Err(error!("unhandled exception '{tag}")),
        }
    }
}

fn proc_arithmetic(ctx: &mut AoclaCtx) -> Result {
    let b_obj = ctx.stack.pop()?;
    let a_obj = ctx.stack.pop()?;

    let (Object::Int(b), Object::Int(a)) = (b_obj, a_obj) else {
        bail!("Both objects must be of type Int");
    };

    let result = match ctx.cur_proc_name()? {
        "+" => a.checked_add(b),
        "-" => a.checked_sub(b),
        "*" => a.checked_mul(b),
        "/" => {
            if b == 0 {
                return ctx.throw("div-by-zero");
            }
            a.checked_div(b)
        }
        _ => unreachable!(),
    };

    match result {
        Some(value) => {
            ctx.stack.push(Object::Int(value));
            Ok(())
        }
        None => ctx.throw("overflow"),
    }
}

fn proc_compare(ctx: &mut AoclaCtx) -> Result {
    let b_obj = ctx.stack.pop()?;
    let a_obj = ctx.stack.pop()?;

    let ord = a_obj.cmp(&b_obj);

    use Ordering::*;
    ctx.stack.push(Object::Bool(match ctx.cur_proc_name()? {
        "=" => ord == Equal,
        "<>" => ord != Equal,
        ">=" => ord == Equal || ord == Greater,
        "<=" => ord == Equal || ord == Less,
        ">" => ord == Greater,
        "<" => ord == Less,
        _ => unreachable!(),
    }));
    Ok(())
}

fn proc_boolean(ctx: &mut AoclaCtx) -> Result {
    let is_unary_op = ctx.cur_proc_name().is_ok_and(|s| s == "not");

    if is_unary_op {
        if let Object::Bool(b) = ctx.stack.pop()? {
            ctx.stack.push(Object::Bool(!b));
        } else {
            bail!("Expected object of type Bool");
        }
    } else {
        let b_obj = ctx.stack.pop()?;
        let a_obj = ctx.stack.pop()?;

        let (Object::Bool(a), Object::Bool(b)) = (a_obj, b_obj) else {
            bail!("Both objects must be of type Bool");
        };

        ctx.stack.push(Object::Bool(match ctx.cur_proc_name()? {
            "and" => a && b,
            "or" => a || b,
            _ => unreachable!(),
        }));
    }
    Ok(())
}

fn proc_concat(ctx: &mut AoclaCtx) -> Result {
    let b_obj = ctx.stack.pop()?;
    let a_obj = ctx.stack.pop()?;

    ctx.stack.push(match (a_obj, b_obj) {
        (Object::Tuple(a, is_quoted), Object::Tuple(b, _)) => {
            Object::Tuple([a, b].concat(), is_quoted)
        }
        (Object::List(a), Object::List(b)) => Object::List([a, b].concat()),
        _ => {
            bail!("Only objects of type List, Tuple or Str can be concatenated")
        }
    });
    Ok(())
}

fn print_proc(ctx: &mut AoclaCtx) -> Result {
    let s = {
        let obj = ctx.stack.peek()?;
        let s: Result<String> = obj.clone().try_into();
        match s {
            Ok(s) => s,
            Err(_) => obj.to_string(),
        }
    };
    print!("{s}");

    let should_print_nl = ctx.cur_proc_name().is_ok_and(|s| s == "println");

    if should_print_nl {
        println!();
    } else {
        io::stdout().flush().map_err(to_error)?;
    }
    Ok(())
}

fn proc_proc(ctx: &mut AoclaCtx) -> Result {
    let Object::Sym(name, _) = ctx.stack.pop()? else {
        bail!("The object naming the procedure must be of type Symbol");
    };

    let body = ctx.stack.pop()?;
    if !matches!(body, Object::List(_)) {
        bail!(
            "The object representing the body of the procedure must be of type List"
        );
    }

    let frame = ctx.frame.clone();

    ctx.add_proc(&name, Proc::Aocla { body, frame });

    Ok(())
}

fn proc_if(ctx: &mut AoclaCtx) -> Result {
    let else_branch = if ctx.cur_proc_name().is_ok_and(|s| s == "if-else") {
        Some(ctx.stack.pop()?)
    } else {
        None
    };

    let if_branch = ctx.stack.pop()?;
    if !matches!(if_branch, Object::List(_)) {
        bail!("`if` branch must be of type List");
    }

    let cond = ctx.stack.pop()?;
    if !matches!(cond, Object::List(_)) {
        bail!(
            "`if` condition must be of type List, that push Bool value to stack"
        );
    }

    ctx.eval(&cond)?;
    let Object::Bool(state) = ctx.stack.pop()? else {
        bail!("`if` condition must push Bool value to stack");
    };

    if state {
        ctx.eval(&if_branch)?;
    } else if let Some(o) = else_branch {
        if !matches!(o, Object::List(_)) {
            bail!("`else` branch must be of type List");
        }
        ctx.eval(&o)?;
    }
    Ok(())
}

fn proc_while(ctx: &mut AoclaCtx) -> Result {
    let body = ctx.stack.pop()?;
    if !matches!(body, Object::List(_)) {
        bail!("`while` body must be of type List");
    }

    let cond = ctx.stack.pop()?;
    if !matches!(cond, Object::List(_)) {
        bail!(
            "`while` condition must be of type List, that push Bool value to stack"
        );
    }

    loop {
        ctx.eval(&cond)?;
        let Object::Bool(state) = ctx.stack.pop()? else {
            bail!("`while` condition must push Bool value to stack");
        };
        if !state {
            break;
        }
        ctx.eval(&body)?;
    }
    Ok(())
}

fn proc_get(ctx: &mut AoclaCtx) -> Result {
    let Object::Int(index) = ctx.stack.pop()? else {
        bail!("Sequences can be indexed only by object of type Int");
    };

    if index.is_negative() {
        return ctx.throw("index-error");
    }

    let index = index as usize;

    match ctx.stack.pop()? {
        Object::List(s) | Object::Tuple(s, _) => {
            if let Some(s) = s.get(index) {
                ctx.stack.push(s.clone());
                Ok(())
            } else {
                ctx.throw("index-error")
            }
        }
        _ => bail!("Only objects of type List, Tuple or Str can be indexed"),
    }
}

fn proc_append(ctx: &mut AoclaCtx) -> Result {
    let b_obj = ctx.stack.pop()?;
    let a_obj = ctx.stack.peek_mut()?;

    let (Object::List(a), b) = (a_obj, b_obj) else {
        bail!("Only objects of type List can use `->` procedure");
    };

    a.push(b);

    Ok(())
}

fn proc_prepend(ctx: &mut AoclaCtx) -> Result {
    let b_obj = ctx.stack.pop()?;
    let a_obj = ctx.stack.peek_mut()?;

    let (Object::List(a), b) = (a_obj, b_obj) else {
        bail!("Only objects of type List can use `<-` procedure");
    };

    a.insert(0, b);

    Ok(())
}

fn proc_len(ctx: &mut AoclaCtx) -> Result {
    match ctx.stack.pop()? {
        Object::List(s) | Object::Tuple(s, _) => {
            ctx.stack.push(Object::Int(s.len() as _))
        }
        _ => {
            bail!("Only objects of type List or Tuple can have length")
        }
    }
    Ok(())
}

fn proc_head(ctx: &mut AoclaCtx) -> Result {
    match ctx.stack.pop()? {
        Object::List(objs) | Object::Tuple(objs, _) => {
            if let Some(obj) = objs.first() {
                ctx.stack.push(obj.clone());
                Ok(())
            } else {
                ctx.throw("index-error")
            }
        }
        _ => bail!("`head` expects list or tuple"),
    }
}

fn proc_tail(ctx: &mut AoclaCtx) -> Result {
    match ctx.stack.pop()? {
        Object::List(objs) | Object::Tuple(objs, _) => {
            if let Some((_, tail)) = objs.split_first() {
                ctx.stack.push(Object::List(tail.to_vec()));
                Ok(())
            } else {
                ctx.throw("index-error")
            }
        }
        _ => bail!("`tail` expects list or tuple"),
    }
}

fn proc_cons(ctx: &mut AoclaCtx) -> Result {
    let seq = ctx.stack.pop()?;
    match &seq {
        Object::List(s) | Object::Tuple(s, _) => {
            let head = s
                .first()
                .ok_or(error!("Unable to take head from empty sequence"))?;
            let tail = s[1..].to_vec();

            ctx.stack.push(head.clone());
            ctx.stack.push(match seq {
                Object::List(_) => Object::List(tail),
                Object::Tuple(_, is_quoted) => Object::Tuple(tail, is_quoted),
                _ => unreachable!(),
            });
        }
        _ => {
            bail!("Only objects of type List or Tuple can use `::` procedure")
        }
    }
    Ok(())
}

fn proc_eval(ctx: &mut AoclaCtx) -> Result {
    let obj @ Object::List(_) = ctx.stack.pop()? else {
        bail!("`eval` expects List to evaluate");
    };
    ctx.eval(&obj)
}

fn proc_catch(ctx: &mut AoclaCtx) -> Result {
    let Object::Tuple(mut tag_pairs, false) = ctx.stack.pop()? else {
        bail!("'catch' expects tuple of handlers ('sym [...])");
    };

    let old_handlers = ctx.handlers.clone();

    if tag_pairs.is_empty() {
        bail!("'catch' expected at least 1 handler");
    }
    loop {
        let Some(sym) = tag_pairs.pop() else {
            break;
        };
        let Object::Sym(sym, true) = sym else {
            bail!("expected handler to have tag (symbol)");
        };
        let Some(handler) = tag_pairs.pop() else {
            bail!("expected even elements count for handler");
        };
        let Object::List(handler_block) = handler else {
            bail!("expected even elements count for handler");
        };
        ctx.handlers.insert(sym, Object::List(handler_block));
    }

    let try_block @ Object::List(_) = ctx.stack.pop()? else {
        bail!("'catch' expects list to try");
    };

    ctx.eval(&try_block)?;
    ctx.handlers = old_handlers;

    Ok(())
}

fn proc_throw(ctx: &mut AoclaCtx) -> Result {
    let Object::Sym(tag, false) = ctx.stack.pop()? else {
        bail!("'throw' expected tag to catch");
    };
    ctx.throw(&tag)
}

fn proc_match(ctx: &mut AoclaCtx) -> Result {
    let Ok(Object::Tuple(mut clauses, _)) = ctx.stack.pop() else {
        bail!("'match' expects tuple of clauses");
    };
    let Ok(Object::Sym(subject, _)) = ctx.stack.pop() else {
        bail!("'match' clause expects subject (sym)");
    };

    while let Some(tag_or_list) = clauses.pop() {
        match tag_or_list {
            Object::Sym(tag, true) => {
                let Some(handler_block @ Object::List(_)) = clauses.pop()
                else {
                    bail!("'match' clause expects List after Symbol tag");
                };

                if subject == tag {
                    ctx.eval(&handler_block)?;
                    return Ok(());
                }
            }
            else_block @ Object::List(_) => {
                ctx.eval(&else_block)?;
                return Ok(());
            }
            _ => bail!("expected handler to have tag (Symbol)"),
        }
    }

    ctx.throw("no-match")
}

fn proc_str(ctx: &mut AoclaCtx) -> Result {
    let object = ctx.stack.pop()?;
    ctx.stack.push(object.to_string().into());
    Ok(())
}

fn proc_int(ctx: &mut AoclaCtx) -> Result {
    let Ok(s): Result<String> = ctx.stack.pop()?.try_into() else {
        bail!("'int' expects Str input");
    };
    match s.parse() {
        Ok(value) => {
            ctx.stack.push(Object::Int(value));
            Ok(())
        }
        Err(_) => ctx.throw("parse-error"),
    }
}

fn proc_read_file(ctx: &mut AoclaCtx) -> Result {
    read_file(ctx, |ctx, content| {
        ctx.stack.push(content.into());
        Ok(())
    })
}

fn proc_read_lines(ctx: &mut AoclaCtx) -> Result {
    read_file(ctx, |ctx, content| {
        ctx.stack.push(Object::List(
            content
                .lines()
                .map(|line| line.to_string().into())
                .collect(),
        ));
        Ok(())
    })
}

fn proc_write_file(ctx: &mut AoclaCtx) -> Result {
    let Ok(filepath): Result<String> = ctx.stack.pop()?.try_into() else {
        bail!("expected filepath (String) to write file");
    };
    let Ok(content): Result<String> = ctx.stack.pop()?.try_into() else {
        bail!("expected content (String) to write file");
    };
    write_file(ctx, &filepath, &content)
}

fn proc_write_lines(ctx: &mut AoclaCtx) -> Result {
    let Ok(filepath): Result<String> = ctx.stack.pop()?.try_into() else {
        bail!("expected filepath (String) to write file");
    };
    let Object::List(lines) = ctx.stack.pop()? else {
        bail!("expected lines (List(String)) to write file");
    };
    let Some(content) =
        lines
            .into_iter()
            .try_fold(String::new(), |mut lines, line| {
                let Ok(line): Result<String> = line.try_into() else {
                    return None;
                };
                lines.push_str(&line);
                Some(lines)
            })
    else {
        bail!("expected lines to be list of strings");
    };
    write_file(ctx, &filepath, &content)
}

fn proc_write_stack(ctx: &mut AoclaCtx) -> Result {
    let stack = ctx.stack.clone();
    let objs = stack.into_inner();

    print!("Stack: ");
    if !objs.is_empty() {
        print!("{}", objs[0]);
        for obj in &objs[1..] {
            print!(", {obj}");
        }
    }
    println!();

    Ok(())
}

fn proc_import(ctx: &mut AoclaCtx) -> Result {
    fn resolve_relative(
        reference_file: impl AsRef<Path>,
        relative: impl AsRef<Path>,
    ) -> PathBuf {
        reference_file
            .as_ref()
            .parent()
            .unwrap()
            .join(relative)
            .components()
            .collect()
    }
    let Ok(filename): Result<String> = ctx.stack.pop()?.try_into() else {
        bail!("'imports' requires filename");
    };
    if !ctx.filename.is_empty() {
        ctx.filename = resolve_relative(&ctx.filename, &filename)
            .to_str()
            .unwrap()
            .to_owned();
    }
    let imported_obj = parse_file(&ctx.filename)?;
    ctx.eval(&imported_obj)
}

fn read_file(
    ctx: &mut AoclaCtx,
    f: impl FnOnce(&mut AoclaCtx, String) -> Result,
) -> Result {
    let Ok(filepath): Result<String> = ctx.stack.pop()?.try_into() else {
        bail!("expected filepath to read file");
    };
    match std::fs::read_to_string(filepath) {
        Ok(content) => f(ctx, content),
        Err(err) => match err.kind() {
            io::ErrorKind::NotFound => ctx.throw("file-not-found"),
            io::ErrorKind::PermissionDenied => ctx.throw("permission-denied"),
            io::ErrorKind::InvalidData => ctx.throw("invalid-data"),
            _ => ctx.throw("io"),
        },
    }
}

fn write_file(ctx: &mut AoclaCtx, filepath: &str, content: &str) -> Result {
    match std::fs::write(filepath, content) {
        Ok(_) => Ok(()),
        Err(err) => match err.kind() {
            io::ErrorKind::AlreadyExists => ctx.throw("file-exist"),
            io::ErrorKind::InvalidInput => ctx.throw("invalid-data"),
            io::ErrorKind::NotFound => ctx.throw("file-not-found"),
            io::ErrorKind::PermissionDenied => ctx.throw("permission-denied"),
            _ => ctx.throw("io"),
        },
    }
}

fn parse_file(filename: impl AsRef<Path>) -> Result<Object> {
    let buf = fs::read_to_string(filename)
        .map_err(|err| error!("Failed to read file: {}", err))?;

    parser::parse_root(&buf).map_err(string_to_error)
}

fn eval_file(filename: impl AsRef<Path>) -> Result {
    let filename = filename.as_ref().to_string_lossy().into_owned().to_string();
    let root_obj = parse_file(&filename)?;
    let mut ctx = AoclaCtx::new()?;
    ctx.filename = filename;
    ctx.eval(&root_obj)?;

    Ok(())
}

fn repl() -> Result {
    let mut ctx = AoclaCtx::new()?;
    loop {
        print!("> ");
        io::stdout().flush().map_err(to_error)?;

        let mut buf = String::new();
        io::stdin().read_line(&mut buf).map_err(to_error)?;

        let result = match buf.trim() {
            "quit" => break,
            code => match parser::parse_root(code) {
                Ok(root_obj) => ctx.eval(&root_obj),
                Err(err) => Err(error!("{}", err)),
            },
        };

        if let Err(err) = result {
            eprintln!("{}", err);
        }
    }
    Ok(())
}

fn main() {
    let result = if let Some(filename) = env::args().nth(1) {
        eval_file(filename)
    } else {
        repl()
    };

    if let Err(err) = result {
        eprintln!("{}", err);
    }
}
