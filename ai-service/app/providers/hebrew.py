"""Everyday Hebrew for the mock: splitting a request into words and numbers, matching a word with its prefixes
and endings ("שגילם" ~ "גיל"), and the words people use to compare ("מתחת ל", "מעל") or to ask ("תביא לי").
Language only - nothing about the business; entity and field names come from the schema context."""

import re
import unicodedata

# Everyday ways to say an operator, longest first within each. The catalog's own labels ("קטן מ") are added
# from the context, so an operator added to the catalog is recognized by its label with no change here.
OPERATOR_WORDS: dict[str, list[str]] = {
    "lte": ["קטן או שווה ל", "לכל היותר"],
    "gte": ["גדול או שווה ל", "לפחות"],
    "isNotEmpty": ["לא ריק", "לא ריקה"],
    "lt": ["נמוך מ", "פחות מ", "מתחת ל", "לפני"],
    "gt": ["גבוה מ", "יותר מ", "מעל", "אחרי"],
    "neq": ["שונה מ"],
    "eq": ["שווה ל"],
    "isEmpty": ["ריק", "ריקה", "חסר", "חסרה"],
    "between": ["בין"],
    "contains": ["מכיל"],
}
EMPTY_BEFORE = {"ללא", "בלי", "חסר", "חסרה", "חסרים", "חסרות", "חסרי"}  # "עובדים ללא טלפון"
NEGATIONS = {"לא", "אינו", "אינה", "אינם", "אינן"}
AND = {"וגם", "גם", "ו"}
OR = {"או"}
RANGE_JOINERS = {"ל", "ו", "עד"}
# Words a request is phrased with that carry no condition.
FILLER = {
    "מצא",
    "מצאי",
    "מצאו",
    "הצג",
    "הציגי",
    "הצגת",
    "הראה",
    "תראה",
    "תראי",
    "תביא",
    "תביאי",
    "הבא",
    "איתור",
    "אתר",
    "לאתר",
    "למצוא",
    "חפש",
    "חפשי",
    "רשימת",
    "רשימה",
    "כל",
    "את",
    "לי",
    "לנו",
    "אשר",
    "עם",
    "של",
    "שלהם",
    "שלהן",
    "שלו",
    "שלה",
    "שבהם",
    "שבהן",
    "בהם",
    "בהן",
    "שהם",
    "שהן",
    "הם",
    "הן",
    "הוא",
    "היא",
    "יש",
    "כאשר",
    "רשומות",
    "בקרה",
    "שמחזירה",
    "שמחזיר",
    "מחזירה",
    "מחזיר",
    "תבנה",
    "בנה",
    "צור",
    "בני",
    "בנות",
}
UNITS = {'ש"ח', "שח", "₪", "שקל", "שקלים", "שנים", "שנה"}
_PREFIXES = "והשבלמכ"

_DASH_NEXT_TO_HEBREW = re.compile(r"(?<=[א-ת])[־‐-―]|[־‐-―](?=[א-ת])")
_TOKEN = re.compile(r'\d{1,2}/\d{1,2}/\d{4}|\d{4}-\d{2}-\d{2}|\d[\d,]*(?:\.\d+)?|[א-ת]+(?:"[א-ת]+)?|₪')
DMY = re.compile(r"(\d{1,2})/(\d{1,2})/(\d{4})")


def words_of(text: str) -> list[str]:
    """Hebrew words, numbers ("15,000"), dates; a dash after "מ" / "ל" splits "מ-18" into "מ", "18"."""
    text = unicodedata.normalize("NFKC", text)
    text = re.sub("[״“”„]", '"', text)
    text = _DASH_NEXT_TO_HEBREW.sub(" ", text)
    return _TOKEN.findall(text)


def variants(token: str) -> list[str]:
    """The word and the word without up to three Hebrew prefix letters: "ושהגיל" -> "שהגיל", "הגיל", "גיל"."""
    variants = [token]
    for i in range(1, min(3, len(token) - 2) + 1):
        if token[i - 1] not in _PREFIXES:
            break
        variants.append(token[i:])
    return variants


def bare(token: str) -> str:
    """A filler or connector may carry a prefix too: "ולי", "שלהם"."""
    return next((v for v in variants(token) if v in FILLER or v in EMPTY_BEFORE), token)


_FINAL_LETTERS = str.maketrans("ךםןףץ", "כמנפצ")


def same_word(token: str, word: str) -> bool:
    """ "שגילם" ~ "גיל", "פעילים" ~ "פעיל", "במחלקה" ~ "מחלקה", "פעילות" ~ "פעילה", "נמוכה" ~ "נמוך":
    a prefix off, a short ending allowed (a word ending in "ה" may drop it, as its plural does); a final
    letter (ך) is the letter it ends a word with (כ)."""
    word = word.translate(_FINAL_LETTERS)
    stems = [word] + ([word[:-1]] if len(word) >= 4 and word.endswith("ה") else [])
    return any(
        v == word or any(len(stem) >= 2 and v.startswith(stem) and len(v) - len(stem) <= 2 for stem in stems)
        for v in (v.translate(_FINAL_LETTERS) for v in variants(token))
    )


def matches(tokens: list[str], i: int, words: tuple[str, ...]) -> bool:
    return i + len(words) <= len(tokens) and all(same_word(tokens[i + k], w) for k, w in enumerate(words))


def request_name(text: str) -> str:
    """The request without the words it was asked with:
    "תביא לי עובדים שגילם מתחת ל-30" -> "עובדים שגילם מתחת ל-30"."""
    words = text.strip().rstrip(".!?").split()
    while words and bare(re.sub(r"[^א-ת]", "", words[0])) in FILLER:
        words.pop(0)
    return " ".join(words)[:200]
