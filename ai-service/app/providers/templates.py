"""The assignment's examples as templates: the same request with other numbers, dates or comparisons -
"נמוך מ־19" instead of "נמוך מ־18", "גבוה" instead of "נמוך", "מתחת ל" instead of "נמוך מ" - gets the prepared
answer with those values, keeping everything else it shows (several conditions, assumptions, the OR group...).

The slots are found in each example by itself, so a new example needs no code: a number or date in its text is
the condition with that value in its answer, and the comparison words just before it are that condition's
operator. Every other word must stay as in the example (spelling differences aside); otherwise it is not this
example, and the mock goes on to recognize the request from the catalog.
"""

import copy
import re
from dataclasses import dataclass

from app.models.schema_context import SchemaContext
from app.models.suggestion import ControlSuggestion, Scalar
from app.providers.hebrew import DMY, OPERATOR_WORDS, UNITS, matches, request_name, same_word, words_of

_COMPARISONS = {"eq", "neq", "gt", "gte", "lt", "lte"}
_NUMBER = re.compile(r"\d[\d,]*(?:\.\d+)?")
_ISO_DATE = re.compile(r"\d{4}-\d{2}-\d{2}")


@dataclass(frozen=True)
class _Slot:
    """A comparison and its value in the example's words, and the condition of the answer they belong to."""

    start: int
    words: int
    """How many words the comparison takes ("נמוך מ" = 2); the value follows them."""
    condition: tuple[int, ...]
    """("conditions", i) as (i,), or ("groups", g, i) as (g, i)."""


@dataclass(frozen=True)
class Template:
    words: tuple[str, ...]
    slots: tuple[_Slot, ...]
    answer: dict
    """The prepared answer, in the contract's JSON form (camelCase)."""


def template(text: str, answer: dict) -> Template | None:
    """The example's slots, or None when it has none (nothing to change) or a value is ambiguous."""
    words = _content(words_of(text))
    conditions = _conditions(answer)
    slots = []
    for i in range(len(words)):
        value, _ = _value(words, i)
        if value is None:
            continue
        before = _comparison_before(words, i)
        holders = [
            path for path, c in conditions if c.get("value") == value and c["operator"] in _COMPARISONS
        ]
        if before is None or len(holders) != 1:
            return None  # a number this example does not explain: keep it an exact-match example
        slots.append(_Slot(i - before, before, holders[0]))
    return Template(tuple(words), tuple(slots), answer) if slots else None


def adapt(template: Template, text: str, context: SchemaContext) -> ControlSuggestion | None:
    """The example's answer with the request's values and comparisons; None if it is not this example."""
    labels = {o.key: o.label for o in context.operators}
    phrases = _phrases(context)
    words = _content(words_of(text))
    answer = copy.deepcopy(template.answer)
    read: list[str] = []
    changed = False
    i = j = 0
    slots = {s.start: s for s in template.slots}
    while i < len(template.words):
        if (slot := slots.get(i)) is not None:
            found = next(((key, p) for key, p in phrases if matches(words, j, p)), None)
            value, used = _value(words, j + len(found[1])) if found else (None, 0)
            if found is None or value is None:
                return None
            key, said = found
            condition = _condition(answer, slot.condition)
            changed |= (condition["operator"], condition["value"]) != (key, value)
            condition["operator"], condition["value"] = key, value
            said_words = " ".join(words[j : j + len(said)])
            if key in labels and said_words != labels[key]:
                read.append(f"'{said_words}' = {labels[key]}")
            i, j = slot.start + slot.words + 1, j + len(said) + used
        elif j < len(words) and words[j] == template.words[i]:
            i, j = i + 1, j + 1
        else:
            return None
    if j != len(words):
        return None

    if changed:  # the prepared name and explanation spoke of the example's values
        answer["name"] = request_name(text)
        answer["description"] = text.strip()[:1000]
        answer["explanation"] = "התאמתי את דוגמת המטלה לערכים שביקשת (הדגמה - Mock)."
        answer["assumptions"] = [*answer.get("assumptions", []), *read][:10]
    return ControlSuggestion.model_validate(answer)


def _content(words: list[str]) -> list[str]:
    """Units add nothing to the match: "15,000 ש"ח" = "15,000"."""
    return [w for w in words if w not in UNITS]


def _value(words: list[str], i: int) -> tuple[Scalar | None, int]:
    """A number ("15,000"; "15 אלף" = 15000) or a date ("01/01/2020" -> "2020-01-01") at `i`; its words."""
    if i >= len(words):
        return None, 0
    word = words[i]
    if _NUMBER.fullmatch(word):
        number = float(word.replace(",", ""))
        thousands = i + 1 < len(words) and same_word(words[i + 1], "אלף")
        number *= 1000 if thousands else 1
        return (int(number) if number.is_integer() else number), 2 if thousands else 1
    if (dmy := DMY.fullmatch(word)) is not None:
        day, month, year = (int(g) for g in dmy.groups())
        return f"{year:04d}-{month:02d}-{day:02d}", 1
    return (word, 1) if _ISO_DATE.fullmatch(word) else (None, 0)


def _comparison_before(words: list[str], i: int) -> int | None:
    """How many words before `i` say a comparison ("גבוהה מ" -> 2; the longest phrase wins), or None."""
    phrases = sorted(
        (tuple(p.split()) for key in _COMPARISONS for p in OPERATOR_WORDS.get(key, [])), key=len, reverse=True
    )
    return next((len(p) for p in phrases if i >= len(p) and matches(words, i - len(p), p)), None)


def _phrases(context: SchemaContext) -> list[tuple[str, tuple[str, ...]]]:
    phrases = {(key, tuple(p.split())) for key in _COMPARISONS for p in OPERATOR_WORDS.get(key, [])}
    phrases |= {(o.key, tuple(o.label.split())) for o in context.operators if o.key in _COMPARISONS}
    return sorted(phrases, key=lambda p: -len(p[1]))


def _conditions(answer: dict) -> list[tuple[tuple[int, ...], dict]]:
    found = [((i,), c) for i, c in enumerate(answer.get("conditions", []))]
    for g, group in enumerate(answer.get("groups", [])):
        found += [((g, i), c) for i, c in enumerate(group["conditions"])]
    return found


def _condition(answer: dict, path: tuple[int, ...]) -> dict:
    if len(path) == 1:
        return answer["conditions"][path[0]]
    return answer["groups"][path[0]]["conditions"][path[1]]
