import { AllowedValue, DataType } from './catalog.models';
import { ControlDefinition } from './control-definition';

export type RunStatus = 'succeeded' | 'failed';

export interface LastRun {
  readonly runAt: string;
  readonly status: RunStatus;
  readonly violationCount: number | null;
  readonly error: string | null;
}

export interface ControlListItem {
  readonly id: number;
  readonly name: string;
  readonly description: string | null;
  readonly rootEntityKey: string;
  readonly rootEntityName: string;
  readonly updatedAt: string;
  readonly lastRun: LastRun | null;
}

export interface Control {
  readonly id: number;
  readonly name: string;
  readonly description: string | null;
  readonly definition: ControlDefinition;
  /** The Hebrew sentence the definition means (or a warning if it no longer fits the catalog). */
  readonly definitionDescription: string;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly lastRun: LastRun | null;
}

/** A new, unsaved control to open in the editor (e.g. an AI suggestion opened with "עריכה בעורך"). */
export interface ControlDraft {
  readonly name: string;
  readonly description: string;
  readonly definition: ControlDefinition;
}

export interface SaveControlRequest {
  readonly name: string;
  readonly description: string | null;
  readonly definition: ControlDefinition;
}

/** A problem in a definition; `path` points at the node, e.g. "filter.children[1].operand". */
export interface DefinitionError {
  readonly path: string;
  readonly code: string;
  readonly message: string;
  /** Only not filled in yet (no field, no value...) - the server decides; not marked while the user is editing. */
  readonly isIncomplete: boolean;
}

export interface ValidateResponse {
  readonly isValid: boolean;
  readonly errors: readonly DefinitionError[];
  readonly description: string | null;
}

// ---------- results ----------

export interface ColumnFilter {
  readonly key: string;
  readonly value: string;
}

export interface ResultsRequest {
  readonly page: number;
  readonly pageSize: number;
  readonly search?: string | null;
  readonly sortKey?: string | null;
  readonly sortDescending?: boolean;
  readonly filters?: readonly ColumnFilter[];
  /** False skips counting the violations and the records checked again (they do not change while browsing). */
  readonly includeSummary?: boolean;
}

export interface ResultColumn {
  readonly key: string;
  readonly displayName: string;
  readonly dataType: DataType;
  readonly unit: string | null;
  readonly isConditionField: boolean;
  /** For enum columns: filter by business label. */
  readonly allowedValues: readonly AllowedValue[];
}

/** One violating record; values are already formatted in Hebrew by the server. */
export interface ResultRow {
  readonly recordId: number;
  readonly title: string;
  readonly values: Readonly<Record<string, string>>;
  readonly reasons: readonly string[];
}

export interface RunResult {
  readonly controlId: number;
  readonly controlName: string;
  readonly rootEntityName: string;
  readonly executedAt: string;
  readonly durationMs: number;
  readonly violationCount: number;
  readonly totalScanned: number;
  /** Rows matching search / filters; null when the page is past the end. */
  readonly totalFiltered: number;
  readonly page: number;
  readonly pageSize: number;
  readonly columns: readonly ResultColumn[];
  readonly rows: readonly ResultRow[];
}

/** As served by `/results`: without the summary when it was not asked for. */
export type ResultsPage = Omit<RunResult, 'violationCount' | 'totalScanned'> & {
  readonly violationCount: number | null;
  readonly totalScanned: number | null;
};
