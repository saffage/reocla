use core::fmt::Display;

use nom::branch::alt;
use nom::bytes::complete::{is_not, tag, take_until, take_while1};
use nom::character::anychar;
use nom::character::complete::{char, i64, multispace1};
use nom::combinator::{cut, map, opt, value, verify};
use nom::error::{context, Error};
use nom::multi::{fold_many0, many0, many1, separated_list0};
use nom::sequence::{delimited, pair, preceded};
use nom::{IResult, Parser};

use crate::error::{bail, error, AoclaError};

pub fn parse_root(i: &str) -> Result<Object, String> {
    let (_, v) = parse_object_sequence(i).map_err(|err| err.to_string())?;
    Ok(Object::List(v))
}

fn parse_object(i: &str) -> IResult<&str, Object> {
    alt((
        map(parse_int, Object::Int),
        map(parse_char, Object::Char),
        map(parse_list, Object::List),
        map(parse_tuple, |(v, q)| Object::Tuple(v, q)),
        map(parse_str, |s| {
            Object::List(s.chars().map(Object::Char).collect())
        }),
        map(parse_bool, Object::Bool),
        map(parse_sym, |(s, q)| Object::Sym(s, q)),
    ))
    .parse(i)
}

fn parse_object_sequence(i: &str) -> IResult<&str, Vec<Object>> {
    whitespace(separated_list0(whitespace1, parse_object), i)
}

fn parse_int(i: &str) -> IResult<&str, i64> {
    i64(i)
}

fn parse_char(i: &str) -> IResult<&str, char> {
    delimited(char('\''), alt((parse_esc_char, anychar)), char('\'')).parse(i)
}

fn parse_list(i: &str) -> IResult<&str, Vec<Object>> {
    delimited(
        char('['),
        parse_object_sequence,
        context("closing list bracket", cut(char(']'))),
    )
    .parse(i)
}

fn parse_tuple(i: &str) -> IResult<&str, (Vec<Object>, bool)> {
    let (s, (q, v)) = pair(
        opt(char('\'')),
        delimited(
            char('('),
            parse_object_sequence,
            context("closing tuple parenthesis", cut(char(')'))),
        ),
    )
    .parse(i)?;
    Ok((s, (v, q.is_some())))
}

fn parse_str(i: &str) -> IResult<&str, String> {
    delimited(
        char('"'),
        fold_many0(parse_str_frag, String::new, |mut s, frag| {
            match frag {
                StringFragment::Lit(lit) => s.push_str(lit),
                StringFragment::EscChar(esc_char) => s.push(esc_char),
            }
            s
        }),
        char('"'),
    )
    .parse(i)
}

fn parse_bool(i: &str) -> IResult<&str, bool> {
    alt((value(false, tag("#f")), value(true, tag("#t")))).parse(i)
}

fn parse_sym(i: &str) -> IResult<&str, (String, bool)> {
    let (s, (q, sym)) = pair(opt(char('\'')), parse_sym_lit).parse(i)?;
    Ok((s, (sym.to_string(), q.is_some())))
}

fn is_sym_char(c: char) -> bool {
    const SYMBOLS: &str = "_@$+-*/=?!%><&|~:";
    c.is_alphanumeric() || SYMBOLS.contains(c)
}

fn is_sym(i: &str) -> IResult<&str, &str> {
    take_while1(is_sym_char).parse(i)
}

fn parse_sym_lit(i: &str) -> IResult<&str, &str> {
    verify(is_sym, |s: &str| !s.is_empty()).parse(i)
}

fn parse_esc_char(i: &str) -> IResult<&str, char> {
    preceded(
        char('\\'),
        alt((
            value('\\', char('\\')),
            value('\'', char('\'')),
            value('"', char('"')),
            value('\n', char('n')),
            value('\r', char('r')),
            value('\t', char('t')),
        )),
    )
    .parse(i)
}

fn parse_str_lit(i: &str) -> IResult<&str, &str> {
    verify(is_not("\"\\"), |s: &str| !s.is_empty()).parse(i)
}

fn parse_str_frag(i: &str) -> IResult<&str, StringFragment<'_>> {
    alt((
        map(parse_str_lit, StringFragment::Lit),
        map(parse_esc_char, StringFragment::EscChar),
    ))
    .parse(i)
}

#[derive(Debug)]
enum StringFragment<'a> {
    Lit(&'a str),
    EscChar(char),
}

fn whitespace<'a, F>(
    inner: F,
    i: &'a str,
) -> IResult<&'a str, F::Output, F::Error>
where
    F: Parser<&'a str, Error = Error<&'a str>>,
{
    delimited(whitespace0, inner, whitespace0).parse(i)
}

fn line_comment(i: &str) -> IResult<&str, ()> {
    value((), pair(char(';'), is_not("\n\r"))).parse(i)
}

fn inline_comment(i: &str) -> IResult<&str, ()> {
    value((), (tag("(*"), take_until("*)"), tag("*)"))).parse(i)
}

fn whitespace0(i: &str) -> IResult<&str, ()> {
    value(
        (),
        many0(alt((
            value((), multispace1),
            value((), line_comment),
            value((), inline_comment),
        ))),
    )
    .parse(i)
}

fn whitespace1(i: &str) -> IResult<&str, ()> {
    value(
        (),
        many1(alt((
            value((), multispace1),
            value((), pair(char(';'), is_not("\n\r"))),
            value((), (tag("(*"), take_until("*)"), tag("*)"))),
        ))),
    )
    .parse(i)
}

#[derive(Debug, Clone, PartialEq, Eq, PartialOrd, Ord)]
pub enum Object {
    Int(i64),
    Char(char),
    List(Vec<Object>),
    Tuple(Vec<Object>, bool),
    Bool(bool),
    Sym(String, bool),
}

impl Display for Object {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let Ok(s): Result<String, AoclaError> = self.clone().try_into() else {
            return match self {
                Self::Int(v) => write!(f, "{v}"),
                Self::Char(c) => write!(f, "{c:?}"),
                Self::List(objects) => {
                    write!(f, "[")?;
                    if !objects.is_empty() {
                        write!(f, "{}", objects[0])?;
                        for object in &objects[1..] {
                            write!(f, ", {object}")?;
                        }
                    }
                    write!(f, "]")
                }
                Self::Tuple(objects, _) => {
                    write!(f, "(")?;
                    if !objects.is_empty() {
                        write!(f, "{}", objects[0])?;
                        for object in &objects[1..] {
                            write!(f, ", {object}")?;
                        }
                    }
                    write!(f, ")")
                }
                Self::Bool(b) => write!(f, "{b}"),
                Self::Sym(s, _) => write!(f, "'{s}"),
            };
        };
        write!(f, "{s:?}")
    }
}

impl From<String> for Object {
    fn from(s: String) -> Self {
        Object::List(s.chars().map(Object::Char).collect())
    }
}

impl TryFrom<Object> for String {
    type Error = AoclaError;

    fn try_from(obj: Object) -> Result<Self, Self::Error> {
        let Object::List(items) = obj else {
            bail!("object is not a string")
        };
        let Some(s) =
            items.into_iter().try_fold(String::new(), |mut acc, item| {
                let Object::Char(c) = item else { return None };
                acc.push(c);
                Some(acc)
            })
        else {
            bail!("list contains non-char item")
        };
        Ok(s)
    }
}
