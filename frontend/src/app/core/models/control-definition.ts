import { ConditionOperator } from './catalog.models';

/**
 * A control definition - the same JSON contract the server validates and stores (PLAN §5.1).
 * Nodes are a discriminated union on `type`, operands on `kind`, so TypeScript checks every branch.
 * Field paths are relative to the root entity: "age", "department.isActive", "manager.salary".
 */
export interface ControlDefinition {
  readonly schemaVersion: 1;
  readonly rootEntity: string;
  readonly filter: GroupNode;
}

export type LogicalOperator = 'and' | 'or';
export type RelatedQuantifier = 'any' | 'none';

export interface GroupNode {
  readonly type: 'group';
  readonly logic: LogicalOperator;
  readonly children: readonly FilterNode[];
}

export interface ConditionNode {
  readonly type: 'condition';
  readonly field: string;
  readonly operator: ConditionOperator;
  readonly operand?: Operand | null;
}

/** "There is (any) / there is no (none) related record matching `where`" over a one-to-many relation. */
export interface RelatedNode {
  readonly type: 'related';
  readonly relation: string;
  readonly quantifier: RelatedQuantifier;
  readonly where?: GroupNode | null;
}

export type FilterNode = GroupNode | ConditionNode | RelatedNode;

export type LiteralScalar = string | number | boolean;
/** A list (IN) or range (BETWEEN); a range being typed may be incomplete (null) - the server reports the missing value. */
export type LiteralValue = LiteralScalar | readonly (LiteralScalar | null)[];

export interface LiteralOperand {
  readonly kind: 'literal';
  readonly value: LiteralValue;
}

/** Compare against another field of the same record. */
export interface FieldOperand {
  readonly kind: 'field';
  readonly field: string;
}

export type Operand = LiteralOperand | FieldOperand;

export const CURRENT_SCHEMA_VERSION = 1;

export function emptyDefinition(rootEntity: string): ControlDefinition {
  return { schemaVersion: CURRENT_SCHEMA_VERSION, rootEntity, filter: { type: 'group', logic: 'and', children: [] } };
}
