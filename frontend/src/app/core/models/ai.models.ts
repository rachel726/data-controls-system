import { ControlDefinition } from './control-definition';
import { DefinitionError } from './control.models';

/** GET /api/ai/examples - a ready-made request the AI answers, offered as a one-click example. */
export interface AiExample {
  readonly label: string;
  readonly text: string;
}

/** POST /api/ai/suggest-control - the AI's proposal, translated and validated, waiting for the user's approval. */
export interface AiSuggestionResult {
  /** False when the AI could not map the request to any condition. */
  readonly understood: boolean;
  readonly name: string;
  readonly description: string | null;
  /** How the AI understood the request. */
  readonly explanation: string;
  readonly assumptions: readonly string[];
  readonly unmappedTerms: readonly string[];
  /** The suggestion translated to a regular control definition (null when not understood). */
  readonly definition: ControlDefinition | null;
  readonly isValid: boolean;
  readonly validationErrors: readonly DefinitionError[];
  /** The definition's Hebrew meaning, when valid. */
  readonly definitionDescription: string | null;
}
