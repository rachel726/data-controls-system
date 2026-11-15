import { AiSuggestionResult } from '../app/core/models/ai.models';

/** A valid AI suggestion as the API returns it (shared by the AI screen's tests). */
export const VALID_SUGGESTION: AiSuggestionResult = {
  understood: true,
  name: 'קטינים עם שכר גבוה',
  description: 'עובדים מתחת לגיל 18',
  explanation: 'חיפשתי עובדים שגילם קטן מ-18.',
  assumptions: ["'משכורת' = שכר חודשי"],
  unmappedTerms: [],
  definition: {
    schemaVersion: 1, rootEntity: 'employee',
    filter: { type: 'group', logic: 'and', children: [{ type: 'condition', field: 'age', operator: 'lt', operand: { kind: 'literal', value: 18 } }] },
  },
  isValid: true,
  validationErrors: [],
  definitionDescription: 'גיל קטן מ-18',
};
