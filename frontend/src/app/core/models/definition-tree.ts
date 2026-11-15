import { FilterNode, GroupNode, LogicalOperator } from './control-definition';

/**
 * Address of a node inside the root group: child indexes, with 'where' stepping into a related node's group.
 * `[1, 'where', 0]` = first condition inside the "related records" node that is the root's second child.
 */
export type NodePath = readonly (number | 'where')[];

/**
 * One change to the tree, emitted by the condition builder and applied by whoever owns the definition
 * (the editor store). Keeps the builder free of state, so it is reusable (e.g. read-only on the AI screen).
 */
export type TreeEdit =
  | { readonly kind: 'add'; readonly groupPath: NodePath; readonly node: FilterNode }
  | { readonly kind: 'update'; readonly path: NodePath; readonly node: FilterNode }
  | { readonly kind: 'remove'; readonly path: NodePath }
  | { readonly kind: 'logic'; readonly groupPath: NodePath; readonly logic: LogicalOperator };

/**
 * What may be added to a group at `depth` (the root group is depth 1) under the server's `maxDepth`:
 * a nested group counts one level; a "related records" node one level and its conditions another;
 * no "related records" inside "related records".
 */
export function allowedAdditions(depth: number, insideRelated: boolean, maxDepth: number): { group: boolean; related: boolean } {
  return {
    group: depth + 1 <= maxDepth,
    related: !insideRelated && depth + 2 <= maxDepth,
  };
}

/**
 * Pure, immutable operations on the condition tree. Every change returns a new tree (unchanged branches are
 * shared), which keeps undo-free dirty tracking trivial and lets signals update only what changed.
 */
export const DefinitionTree = {
  /** Replaces the node at `path` with `update(node)`. */
  update(root: GroupNode, path: NodePath, update: (node: FilterNode) => FilterNode): GroupNode {
    if (path.length === 0) {
      return asGroup(update(root));
    }

    const [step, ...rest] = path;
    if (step === 'where') {
      throw new Error('A path cannot start with "where".');
    }

    return {
      ...root,
      children: root.children.map((child, index) => (index === step ? updateNode(child, rest, update) : child)),
    };
  },

  get(root: GroupNode, path: NodePath): FilterNode | undefined {
    let node: FilterNode | undefined = root;
    for (const step of path) {
      if (step === 'where') {
        node = node?.type === 'related' ? (node.where ?? undefined) : undefined;
      } else {
        node = node?.type === 'group' ? node.children[step] : undefined;
      }
    }
    return node;
  },

  append(root: GroupNode, groupPath: NodePath, node: FilterNode): GroupNode {
    return DefinitionTree.update(root, groupPath, (group) => ({ ...asGroup(group), children: [...asGroup(group).children, node] }));
  },

  remove(root: GroupNode, path: NodePath): GroupNode {
    if (path.length === 0) {
      throw new Error('The root group cannot be removed.');
    }

    const index = path[path.length - 1];
    if (index === 'where') {
      throw new Error('Remove the related node instead of its "where" group.');
    }

    return DefinitionTree.update(root, path.slice(0, -1), (parent) => {
      const group = asGroup(parent);
      return { ...group, children: group.children.filter((_, i) => i !== index) };
    });
  },

  setLogic(root: GroupNode, groupPath: NodePath, logic: LogicalOperator): GroupNode {
    return DefinitionTree.update(root, groupPath, (group) => ({ ...asGroup(group), logic }));
  },

  /** Applies one edit emitted by the condition builder. */
  apply(root: GroupNode, edit: TreeEdit): GroupNode {
    switch (edit.kind) {
      case 'add':
        return DefinitionTree.append(root, edit.groupPath, edit.node);
      case 'update':
        return DefinitionTree.update(root, edit.path, () => edit.node);
      case 'remove':
        return DefinitionTree.remove(root, edit.path);
      case 'logic':
        return DefinitionTree.setLogic(root, edit.groupPath, edit.logic);
    }
  },

  /** The server's error path for a node: [1, 'where', 0] → "filter.children[1].where.children[0]". */
  toErrorPath(path: NodePath): string {
    let result = 'filter';
    for (const step of path) {
      result += step === 'where' ? '.where' : `.children[${step}]`;
    }
    return result;
  },

  /**
   * The server errors about the node at `path` itself (its field, operator, value...), not about its children:
   * "filter.children[1].operand.value" is about node [1]; "filter.children[1].children[0]" is about its child.
   */
  errorsAbout<E extends { readonly path: string }>(errors: readonly E[], path: NodePath): E[] {
    const nodePath = DefinitionTree.toErrorPath(path);
    return errors.filter((e) => {
      if (e.path === nodePath) {
        return true;
      }
      if (!e.path.startsWith(nodePath + '.')) {
        return false;
      }
      const property = e.path.slice(nodePath.length + 1);
      return !property.startsWith('children[') && !property.startsWith('where');
    });
  },

  /** Counts leaves (conditions and related-records conditions), like the server's condition limit. */
  countConditions(root: GroupNode): number {
    return root.children.reduce((sum, child) => {
      if (child.type === 'group') {
        return sum + DefinitionTree.countConditions(child);
      }
      return sum + 1;
    }, 0);
  },
};

function updateNode(node: FilterNode, path: NodePath, update: (node: FilterNode) => FilterNode): FilterNode {
  if (path.length === 0) {
    return update(node);
  }

  const [step, ...rest] = path;
  if (step === 'where') {
    if (node.type !== 'related') {
      throw new Error('"where" applies to related-records nodes only.');
    }
    const where = node.where ?? { type: 'group', logic: 'and', children: [] };
    return { ...node, where: asGroup(updateNode(where, rest, update)) };
  }

  const group = asGroup(node);
  return { ...group, children: group.children.map((child, index) => (index === step ? updateNode(child, rest, update) : child)) };
}

function asGroup(node: FilterNode): GroupNode {
  if (node.type !== 'group') {
    throw new Error(`Expected a group, got "${node.type}".`);
  }
  return node;
}
