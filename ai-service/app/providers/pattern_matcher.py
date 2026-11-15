"""Recognizes simple requests from the business catalog alone - the mock's answer to anything that is not
one of the assignment's examples: "עובדים שהגיל שלהם מתחת ל-30" -> age lt 30, "מתחת ל-20" -> age lt 20.

Entity, field and relation names, data types, allowed values and yes/no wording all come from the schema
context the model is given - nothing about the business is written here. The only fixed words are everyday
Hebrew: how people say "less than" or "without", and request words such as "תביא לי". It answers only when it
accounts for every word of the request; otherwise it returns no suggestion and the words it could not place.

It is not a language model and does not try to be one: it shows that an answer that changes with the request
flows through the same contract, validation, translation and approval as a real model's would.
"""

import re
from dataclasses import dataclass, field

from app.models.schema_context import EntityContext, FieldContext, SchemaContext
from app.models.suggestion import ControlSuggestion, Scalar, SuggestedCondition
from app.providers.hebrew import (
    AND,
    DMY,
    EMPTY_BEFORE,
    FILLER,
    NEGATIONS,
    OPERATOR_WORDS,
    OR,
    RANGE_JOINERS,
    UNITS,
    bare,
    matches,
    request_name,
    same_word,
    variants,
    words_of,
)


@dataclass(frozen=True)
class _Field:
    """A field a condition may use, reached from the root: "salary" or "department.isActive"."""

    path: str
    field: FieldContext
    words: tuple[str, ...]
    depth: int
    owner: tuple[str, ...]
    """The record it is said of: the root's name ("עובד") or the relation's ("מנהל ישיר")."""


@dataclass
class _Parse:
    conditions: list[SuggestedCondition] = field(default_factory=list)
    assumptions: list[str] = field(default_factory=list)
    unknown: list[str] = field(default_factory=list)
    joined_by_and: bool = False
    joined_by_or: bool = False


def recognize(text: str, context: SchemaContext) -> tuple[ControlSuggestion | None, list[str]]:
    """The suggestion when every word is accounted for; otherwise None and the words that were not."""
    tokens = words_of(text)
    root = _root_entity(tokens, context)
    if root is None:
        return None, _meaningful(tokens)

    labels = {o.key: o.label for o in context.operators}
    operators = _operator_phrases(context)
    fields = _reachable_fields(root, context)
    parse = _Parse()
    pending_empty = False
    i = 0
    while i < len(tokens):
        token = tokens[i]
        if (matched := _boolean_mention(tokens, i, fields)) is not None:
            target, value, used = matched
            parse.conditions.append(SuggestedCondition(field=target.path, operator="eq", value=value))
            i += used
        elif (target := _longest_field(tokens, i, fields)) is not None:
            target, end = _owned(tokens, i + len(target.words), target, fields)
            i = _condition(tokens, i, end, target, pending_empty, operators, labels, context, parse)
            pending_empty = False
        elif _is_entity(token, root):
            i += 1
        elif (target := _field_by_first_word(tokens, i, fields)) is not None:
            parse.assumptions.append(f"'{token}' = {target.field.display_name}")
            target, end = _owned(tokens, i + 1, target, fields)
            i = _condition(tokens, i, end, target, pending_empty, operators, labels, context, parse)
            pending_empty = False
        elif bare(token) in EMPTY_BEFORE:
            pending_empty = True
            i += 1
        elif token in OR:
            parse.joined_by_or = True
            i += 1
        elif token in AND:
            parse.joined_by_and = True
            i += 1
        elif bare(token) in FILLER or token in UNITS:
            i += 1
        else:
            parse.unknown.append(token)
            i += 1

    if parse.unknown or not parse.conditions or (parse.joined_by_or and parse.joined_by_and) or pending_empty:
        return None, parse.unknown
    return (
        ControlSuggestion(
            name=request_name(text),
            description=text.strip()[:1000],
            root_entity=root.key,
            logic="or" if parse.joined_by_or else "and",
            conditions=parse.conditions,
            explanation="זיהיתי את הבקשה לפי שמות השדות והפעולות שבמבנה הנתונים (הדגמה - Mock). "
            "מודל AI אמיתי יקבל את אותו מבנה נתונים ויבין גם ניסוחים חופשיים יותר.",
            assumptions=parse.assumptions[:10],
        ),
        [],
    )


def _meaningful(tokens: list[str]) -> list[str]:
    return [t for t in tokens if bare(t) not in FILLER and t not in UNITS and t not in AND | OR][:10]


# ---------- catalog ----------


def _root_entity(tokens: list[str], context: SchemaContext) -> EntityContext | None:
    """The records being checked: the entity named first ("עובדים", "פרויקט")."""
    for token in tokens:
        for entity in context.entities:
            if _is_entity(token, entity):
                return entity
    return None


def _is_entity(token: str, entity: EntityContext) -> bool:
    return any(v in (entity.display_name, entity.display_name_plural) for v in variants(token))


def _reachable_fields(root: EntityContext, context: SchemaContext) -> list[_Field]:
    """The root's fields, then those reached through many-to-one relations (up to the context's limit);
    longest names first ("תאריך סיום העסקה" before a shorter name), the root's own field first on a tie."""
    entities = {e.key: e for e in context.entities}
    fields: list[_Field] = []

    def walk(entity: EntityContext, prefix: str, depth: int, owner: tuple[str, ...]) -> None:
        fields.extend(
            _Field(prefix + f.key, f, tuple(f.display_name.split()), depth, owner) for f in entity.fields
        )
        if depth < context.max_relation_hops:
            for relation in (r for r in entity.relations if r.cardinality == "manyToOne"):
                if (target := entities.get(relation.target_entity)) is not None:
                    walk(target, f"{prefix}{relation.key}.", depth + 1, tuple(relation.display_name.split()))

    walk(root, "", 0, tuple(root.display_name.split()))
    return sorted(fields, key=lambda f: (-len(f.words), f.depth))


def _operator_phrases(context: SchemaContext) -> list[tuple[str, tuple[str, ...]]]:
    phrases = {(key, tuple(p.split())) for key, words in OPERATOR_WORDS.items() for p in words}
    phrases |= {(o.key, tuple(o.label.split())) for o in context.operators}
    return sorted(phrases, key=lambda p: -len(p[1]))


# ---------- conditions ----------


def _longest_field(tokens: list[str], i: int, fields: list[_Field]) -> _Field | None:
    return next((f for f in fields if matches(tokens, i, f.words)), None)


def _field_by_first_word(tokens: list[str], i: int, fields: list[_Field]) -> _Field | None:
    """A longer name said by its first word ("ושכרם" for "שכר חודשי") - only when that word names one field
    nearest the root ("תאריך" or "שם" alone name several, so they are not guessed)."""
    named = [f for f in fields if len(f.words) > 1 and same_word(tokens[i], f.words[0])]
    nearest = [f for f in named if f.depth == min((f.depth for f in named), default=0)]
    return nearest[0] if len(nearest) == 1 else None


def _owned(tokens: list[str], j: int, target: _Field, fields: list[_Field]) -> tuple[_Field, int]:
    """ "שכר חודשי של מנהל ישיר": the same name said of a related record picks that record's field."""
    if j < len(tokens) and tokens[j] == "של":
        for other in fields:
            if other.words == target.words and other.depth > 0 and matches(tokens, j + 1, other.owner):
                return other, j + 1 + len(other.owner)
    return target, j


def _negation(token: str) -> bool:
    return any(v in NEGATIONS for v in variants(token))


def _boolean_mention(tokens: list[str], i: int, fields: list[_Field]) -> tuple[_Field, bool, int] | None:
    """A yes/no field named, or said of its record, maybe negated: "עובדים פעילים", "במחלקה לא פעילה",
    "מחלקה שאינה פעילה" - by its name ("מחלקה פעילה") or by its record + the catalog's yes / no wording."""
    for target in (f for f in fields if f.field.data_type == "boolean"):
        phrasings = [target.words]
        if target.field.true_text:
            phrasings.append(target.owner + tuple(target.field.true_text.split()))
        for words in phrasings:
            if matches(tokens, i, words):
                return target, True, len(words)
            head, last = words[:-1], words[-1:]
            k = i + len(head)
            if (
                head
                and matches(tokens, i, head)
                and k < len(tokens)
                and _negation(tokens[k])
                and matches(tokens, k + 1, last)
            ):
                return target, False, len(words) + 1
        if target.field.false_text:
            words = target.owner + tuple(target.field.false_text.split())
            if matches(tokens, i, words):
                return target, False, len(words)
    return None


def _condition(
    tokens: list[str],
    start: int,
    j: int,
    target: _Field,
    empty_before: bool,
    operators: list[tuple[str, tuple[str, ...]]],
    labels: dict[str, str],
    context: SchemaContext,
    parse: _Parse,
) -> int:
    """After a field named at `start`: [filler] operator value. Returns where the next clause starts."""
    while j < len(tokens) and bare(tokens[j]) in FILLER:
        j += 1
    if empty_before:
        parse.conditions.append(SuggestedCondition(field=target.path, operator="isEmpty"))
        return j

    allowed = {o.key: o for o in context.operators if target.field.data_type in o.allowed_types}
    for key, words in operators:
        operator = allowed.get(key)
        if operator is None:
            continue
        if matches(tokens, j, words):
            said = " ".join(tokens[j : j + len(words)])
            j += len(words)
        elif (glued := _glued_preposition(tokens, j, words)) is not None:
            tokens, said = glued  # "שווה לפעיל": the value without the "ל" it is written with
            j += len(words) - 1
        else:
            continue
        if operator.shape == "none":
            value: Scalar | list[Scalar] | None = None
        elif operator.shape == "value":
            value, j = _value(tokens, j, target.field)
        elif operator.shape == "range":
            low, j = _value(tokens, j, target.field)
            if j < len(tokens) and tokens[j] in RANGE_JOINERS:
                j += 1
            high, j = _value(tokens, j, target.field)
            value = [low, high] if low is not None and high is not None else None
        else:
            value = None  # a list ("אחד מ") is left to a real model
        if operator.shape != "none" and value is None:
            parse.unknown.append(said)
            return j
        parse.conditions.append(SuggestedCondition(field=target.path, operator=key, value=value))
        natural_for_dates = target.field.data_type == "date" and said in ("לפני", "אחרי")
        if said != labels.get(key, said) and not natural_for_dates:
            parse.assumptions.append(f"'{said}' = {labels[key]}")
        return j

    if target.field.data_type == "enum" and "eq" in allowed:  # "בסטטוס מתוכנן": a list value names itself
        value, after = _value(tokens, j, target.field)
        if value is not None:
            parse.conditions.append(SuggestedCondition(field=target.path, operator="eq", value=value))
            return after

    parse.unknown.extend(tokens[start:j])  # a field with nothing said about it - quoted as the user wrote it
    return j


def _glued_preposition(tokens: list[str], j: int, words: tuple[str, ...]) -> tuple[list[str], str] | None:
    """An operator ending in a one-letter preposition written onto its value: "שווה לפעיל", "מתחת ל30"."""
    head, letter = words[:-1], words[-1]
    k = j + len(head)
    if (
        len(letter) == 1
        and head
        and matches(tokens, j, head)
        and k < len(tokens)
        and tokens[k].startswith(letter)
        and len(tokens[k]) > 1
    ):
        return [*tokens[:k], tokens[k][1:], *tokens[k + 1 :]], " ".join([*tokens[j:k], letter])
    return None


def _value(tokens: list[str], j: int, target: FieldContext) -> tuple[Scalar | None, int]:
    """The value after an operator, in the field's type: 30, 15,000 (or "15 אלף"), 01/01/2024, an allowed
    value's label, or one word of text. Units after it ("ש"ח", "שנים") are skipped by the caller."""
    if j >= len(tokens):
        return None, j
    token = tokens[j]
    match target.data_type:
        case "integer" | "decimal" if re.fullmatch(r"\d[\d,]*(?:\.\d+)?", token):
            number = float(token.replace(",", ""))
            if j + 1 < len(tokens) and same_word(tokens[j + 1], "אלף"):
                number, j = number * 1000, j + 1
            return (int(number) if number.is_integer() else number), j + 1
        case "date":
            if (dmy := DMY.fullmatch(token)) is not None:
                day, month, year = (int(g) for g in dmy.groups())
                return f"{year:04d}-{month:02d}-{day:02d}", j + 1
            if re.fullmatch(r"\d{4}-\d{2}-\d{2}", token):
                return token, j + 1
        case "enum":
            for allowed in target.allowed_values:
                words = tuple(allowed.label.split())
                if matches(tokens, j, words):
                    return allowed.value, j + len(words)
        case "text" if not token[0].isdigit():
            return token, j + 1
    return None, j
