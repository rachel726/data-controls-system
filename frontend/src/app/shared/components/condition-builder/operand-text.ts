import { OperandShape } from '../../../core/models/catalog.models';
import { LiteralScalar, Operand } from '../../../core/models/control-definition';
import { FieldOption } from '../../../core/services/catalog.store';

/**
 * A condition's value as business text, for read-only display: "22,000 ₪", "כן", "פעיל, מתוכנן",
 * "01/01/2020 ל-31/12/2020", or the compared field's label. Empty when there is no value.
 */
export function operandText(
  field: FieldOption,
  shape: OperandShape,
  operand: Operand | null | undefined,
  fieldLabel: (path: string) => string,
): string {
  if (!operand) {
    return '';
  }
  if (operand.kind === 'field') {
    return fieldLabel(operand.field);
  }

  const value = operand.value;
  if (!Array.isArray(value)) {
    return scalarText(field, value as LiteralScalar);
  }
  return shape === 'range'
    ? `${scalarText(field, value[0] ?? null)} ${attach('ל', scalarText(field, value[1] ?? null))}`
    : value.map((v) => scalarText(field, v)).join(', ');
}

/**
 * The operator and the value worded together, by the same rules as the server's Hebrew sentences
 * (HebrewPhrases): "קטן מ-18", "גדול משכר חודשי", ": כן", "אחד מ: פעיל, מתוכנן", "בין 5,000 ₪ ל-9,000 ₪", "ריק",
 * "שווה ל"רחל"", and ": ריק" after a yes/no field (named as a statement: "עובד פעיל: ריק").
 */
export function conditionPhrase(operatorLabel: string, shape: OperandShape, isYesNoValue: boolean, valueText: string, isYesNoField = false): string {
  if (shape === 'none' || !valueText) {
    return isYesNoField && shape === 'none' ? `: ${operatorLabel}` : operatorLabel;
  }
  if (isYesNoValue) {
    return `: ${valueText}`;
  }
  if (shape === 'list') {
    return `${operatorLabel}: ${valueText}`;
  }
  return shape === 'range' ? `${operatorLabel} ${valueText}` : attach(operatorLabel, valueText);
}

/** The Hebrew prefixes "מ"/"ל" join a word or a quoted value directly ("משכר", "ל"רחל""), a number or symbol with a hyphen ("מ-18"). */
function attach(label: string, value: string): string {
  const isPrefix = label === 'מ' || label === 'ל' || label.endsWith(' מ') || label.endsWith(' ל');
  if (!isPrefix) {
    return `${label} ${value}`;
  }
  return /^[א-ת"]/.test(value) ? `${label}${value}` : `${label}-${value}`;
}

function scalarText(field: FieldOption, value: LiteralScalar | null): string {
  if (value === null) {
    return '…';
  }

  const { dataType, allowedValues, unit } = field.field;
  switch (dataType) {
    case 'boolean':
      return value ? 'כן' : 'לא';
    case 'enum':
      return allowedValues.find((a) => a.value === value)?.label ?? String(value);
    case 'date': {
      const [y, m, d] = String(value).split('-');
      return d ? `${d}/${m}/${y}` : String(value);
    }
    case 'integer':
    case 'decimal': {
      const text = typeof value === 'number' ? value.toLocaleString('en-US', { maximumFractionDigits: 2 }) : String(value);
      return unit ? `${text} ${unit}` : text;
    }
    default:
      return `"${value}"`;
  }
}
