import { ConditionNode, GroupNode, RelatedNode } from './control-definition';
import { DefinitionTree, allowedAdditions } from './definition-tree';

const age: ConditionNode = { type: 'condition', field: 'age', operator: 'lt', operand: { kind: 'literal', value: 18 } };
const email: ConditionNode = { type: 'condition', field: 'email', operator: 'isEmpty' };
const phone: ConditionNode = { type: 'condition', field: 'phone', operator: 'isEmpty' };
const related: RelatedNode = { type: 'related', relation: 'employees', quantifier: 'none' };

/** root: [age, (email OR phone), related] */
const tree: GroupNode = {
  type: 'group',
  logic: 'and',
  children: [age, { type: 'group', logic: 'or', children: [email, phone] }, related],
};

describe('DefinitionTree', () => {
  it('updates a nested node and leaves the original tree untouched', () => {
    const updated = DefinitionTree.update(tree, [1, 0], () => ({ ...email, field: 'fullName' }));

    expect((DefinitionTree.get(updated, [1, 0]) as ConditionNode).field).toBe('fullName');
    expect((DefinitionTree.get(tree, [1, 0]) as ConditionNode).field).toBe('email');   // immutable
    expect(updated.children[0]).toBe(tree.children[0]);                               // unchanged branches shared
    expect(updated.children[2]).toBe(tree.children[2]);
  });

  it('appends to the root and to a nested group', () => {
    expect(DefinitionTree.append(tree, [], email).children.length).toBe(4);
    expect((DefinitionTree.append(tree, [1], age).children[1] as GroupNode).children.length).toBe(3);
  });

  it('steps into a related node, creating its where-group on first use', () => {
    const withWhere = DefinitionTree.append(tree, [2, 'where'], { type: 'condition', field: 'isActive', operator: 'eq', operand: { kind: 'literal', value: true } });

    const where = (withWhere.children[2] as RelatedNode).where!;
    expect(where.logic).toBe('and');
    expect((where.children[0] as ConditionNode).field).toBe('isActive');
    expect(DefinitionTree.get(withWhere, [2, 'where', 0])).toBe(where.children[0]);
  });

  it('removes nodes by path but never the root or a where-group itself', () => {
    expect(DefinitionTree.remove(tree, [1, 1]).children[1]).toEqual({ type: 'group', logic: 'or', children: [email] });
    expect(DefinitionTree.remove(tree, [0]).children.length).toBe(2);
    expect(() => DefinitionTree.remove(tree, [])).toThrow();
    expect(() => DefinitionTree.remove(tree, [2, 'where'])).toThrow();
  });

  it('switches a group between all / any', () => {
    expect((DefinitionTree.setLogic(tree, [1], 'and').children[1] as GroupNode).logic).toBe('and');
    expect(DefinitionTree.setLogic(tree, [], 'or').logic).toBe('or');
  });

  it('maps paths to the server error format', () => {
    expect(DefinitionTree.toErrorPath([])).toBe('filter');
    expect(DefinitionTree.toErrorPath([1, 0])).toBe('filter.children[1].children[0]');
    expect(DefinitionTree.toErrorPath([2, 'where', 0])).toBe('filter.children[2].where.children[0]');
  });

  it('counts conditions like the server (related = one, groups are not conditions)', () => {
    expect(DefinitionTree.countConditions(tree)).toBe(4);
    expect(DefinitionTree.countConditions({ type: 'group', logic: 'and', children: [] })).toBe(0);
  });

  it('applies the edits emitted by the condition builder', () => {
    const added = DefinitionTree.apply(tree, { kind: 'add', groupPath: [2, 'where'], node: email });
    expect(DefinitionTree.get(added, [2, 'where', 0])).toBe(email);
    expect(DefinitionTree.apply(tree, { kind: 'update', path: [0], node: phone }).children[0]).toBe(phone);
    expect(DefinitionTree.apply(tree, { kind: 'remove', path: [0] }).children.length).toBe(2);
    expect(DefinitionTree.apply(tree, { kind: 'logic', groupPath: [1], logic: 'and' }).children[1]).toMatchObject({ logic: 'and' });
  });

  it('allows nesting like the server: a group is one level, related records two, none inside related records', () => {
    expect(allowedAdditions(1, false, 3)).toEqual({ group: true, related: true });
    expect(allowedAdditions(2, false, 3)).toEqual({ group: true, related: false });
    expect(allowedAdditions(3, false, 3)).toEqual({ group: false, related: false });
    expect(allowedAdditions(1, true, 3)).toEqual({ group: true, related: false });
    expect(allowedAdditions(2, false, 4)).toEqual({ group: true, related: true });   // follows the server's limit
  });

  it('rejects impossible paths', () => {
    expect(() => DefinitionTree.update(tree, [0, 0], (n) => n)).toThrow();          // a condition has no children
    expect(() => DefinitionTree.update(tree, [0, 'where'], (n) => n)).toThrow();    // only related nodes have where
    expect(DefinitionTree.get(tree, [9])).toBeUndefined();
  });
});
