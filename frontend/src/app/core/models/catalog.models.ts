/** Business catalog as served by GET /api/catalog/* - business names only, never tables or columns. */

export type DataType = 'text' | 'integer' | 'decimal' | 'date' | 'boolean' | 'enum';
export type Cardinality = 'manyToOne' | 'oneToMany';
export type OperandShape = 'none' | 'value' | 'range' | 'list';

export interface AllowedValue {
  readonly value: string;
  readonly label: string;
}

export interface FieldDto {
  readonly key: string;
  readonly displayName: string;
  readonly description: string | null;
  readonly dataType: DataType;
  readonly isRequiredByBusiness: boolean;
  readonly allowedValues: readonly AllowedValue[];
  readonly unit: string | null;
}

export interface RelationDto {
  readonly key: string;
  readonly displayName: string;
  readonly description: string | null;
  readonly targetEntityKey: string;
  readonly cardinality: Cardinality;
}

export interface EntityDto {
  readonly key: string;
  readonly displayName: string;
  readonly displayNamePlural: string;
  readonly description: string | null;
  readonly fields: readonly FieldDto[];
  readonly relations: readonly RelationDto[];
  /** The fields a control on this entity may use, listed and named by the server. */
  readonly fieldPaths: readonly FieldPathDto[];
}

/** "department.name" = "שם המחלקה", owned by "מחלקה": field `fieldKey` of entity `entityKey`. */
export interface FieldPathDto {
  readonly path: string;
  readonly label: string;
  readonly owner: string;
  readonly entityKey: string;
  readonly fieldKey: string;
}

/** The limits of a control (GET /api/catalog/limits). The UI uses them to guide the user; the server enforces them. */
export interface LimitsDto {
  readonly maxConditions: number;
  readonly maxDepth: number;
  readonly maxRelationHops: number;
  /** A text value in a condition. */
  readonly maxTextValueLength: number;
  /** Search and column-filter text on the results. */
  readonly maxSearchLength: number;
  readonly maxNameLength: number;
  readonly maxDescriptionLength: number;
  readonly maxAiTextLength: number;
}

export interface OperatorDto {
  readonly key: ConditionOperator;
  readonly label: string;
  readonly dateLabel: string | null;
  readonly allowedTypes: readonly DataType[];
  readonly shape: OperandShape;
  readonly allowsFieldOperand: boolean;
}

export type ConditionOperator =
  | 'eq'
  | 'neq'
  | 'gt'
  | 'gte'
  | 'lt'
  | 'lte'
  | 'between'
  | 'in'
  | 'contains'
  | 'isEmpty'
  | 'isNotEmpty';
