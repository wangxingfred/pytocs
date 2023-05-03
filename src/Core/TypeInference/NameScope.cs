#region License

//  Copyright 2015-2021 John Källén
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//      http://www.apache.org/licenses/LICENSE-2.0
// 
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Pytocs.Core.Types;
using Pytocs.Core.Syntax;

namespace Pytocs.Core.TypeInference
{
    /// <summary>
    /// Implements a scope, which maps names to sets of bindings.
    /// </summary>
    public class NameScope
    {
        public IDictionary<string, ISet<Binding>> table = new Dictionary<string, ISet<Binding>>(0);
        public IDictionary<string, DataType> DataTypes { get; } = new Dictionary<string, DataType>();
        public NameScope? Parent { get; set; } // all are non-null except global table
        public NameScope? Forwarding { get; set; } // link to the closest non-class scope, for lifting functions out
        private List<NameScope>? superClasses;
        private ISet<string>? globalNames;
        private ISet<string>? localNames;
        public NameScopeType stateType { get; set; }
        public string? Path { get; set; }

        public string? DebugName { get; set; }

        /// <summary>
        /// The data type of this scope.
        /// </summary>
        public DataType? DataType { get; set; }

        public NameScope(NameScope? parent, NameScopeType type)
        {
            Parent = parent;
            stateType = type;
            Path = "";

            if (type == NameScopeType.CLASS)
            {
                Forwarding = parent?.Forwarding;
            }
            else
            {
                Forwarding = this;
            }
        }

        public NameScope(NameScope s)
        {
            table = new Dictionary<string, ISet<Binding>>(s.table);
            Parent = s.Parent;
            stateType = s.stateType;
            Forwarding = s.Forwarding;
            superClasses = s.superClasses;
            globalNames = s.globalNames;
            localNames = s.localNames;
            DataType = s.DataType;
            Path = s.Path;
        }

        // erase and overwrite this to s's contents
        public void Overwrite(NameScope s)
        {
            table = s.table;
            Parent = s.Parent;
            stateType = s.stateType;
            Forwarding = s.Forwarding;
            superClasses = s.superClasses;
            globalNames = s.globalNames;
            localNames = s.localNames;
            DataType = s.DataType;
            Path = s.Path;
        }

        public NameScope Clone()
        {
            return new NameScope(this);
        }

        public void Merge(NameScope other)
        {
            foreach (var e2 in other.table)
            {
                var b1 = table.ContainsKey(e2.Key) ? table[e2.Key] : null;
                ISet<Binding> b2 = e2.Value;

                if (b1 != null && b2 != null)
                {
                    b1.UnionWith(b2);
                }
                else if (b1 == null && b2 != null)
                {
                    table[e2.Key] = b2;
                }
            }
        }

        public static NameScope Merge(NameScope state1, NameScope state2)
        {
            var ret = state1.Clone();
            ret.Merge(state2);
            return ret;
        }

        /// <summary>
        /// Add add reference to a superclass scope to this scope.
        /// </summary>
        /// <param name="sup"></param>
        public void AddSuperClass(NameScope sup)

        {
            if (superClasses == null)
            {
                superClasses = new List<NameScope>();
            }

            superClasses.Add(sup);
        }

        public void AddGlobalName(string name)
        {
            if (globalNames == null)
            {
                globalNames = new HashSet<string>();
            }

            globalNames.Add(name);
        }

        public void AddLocalName(string name)
        {
            localNames ??= new HashSet<string>();
            localNames.Add(name);
        }

        public bool IsGlobalName(string name)
        {
            // if (globalNames != null)
            // {
            //     return globalNames.Contains(name);
            // }
            // else if (Parent != null)
            // {
            //     return Parent.IsGlobalName(name);
            // }
            // else
            // {
            //     return false;
            // }
            return !IsLocalName(name);
        }

        public bool IsLocalName(string name)
        {
            // if (localNames == null) return false;
            // if (!localNames.Contains(name)) return false;

            // localNames存在的意义：在还未绑定的时候，就先标记局部变量，可用于后续判断

            // 为什么要判断table.ConstainsKey(name)呢？
            // 因为部分代码（比如TypeCollector.BindParameters）会执行绑定操作，但是不会执行AddLocalName操作
            // 目前不知道有多少这样的代码，所以先这样处理
            return (localNames != null && localNames.Contains(name)) || table.ContainsKey(name);
        }

        public void Remove(string id)
        {
            table.Remove(id);
        }

        public Binding AddModuleBinding(Analyzer analyzer, string id, Module node, DataType type, BindingKind kind)
        {
            var b = analyzer.CreateBinding(id, node, type, kind);

            if (type is ModuleType mt)
            {
                b.QName = mt.qname;
            }
            else
            {
                b.QName = ExtendPath(Path, id);
            }

            SetIdentifierBinding(id, b);
            return b;
        }

        public Binding AddLocalBinding(Analyzer analyzer, string id, Exp node, DataType type, BindingKind kind)
        {
            var b = analyzer.CreateBinding(id, node, type, kind);
            if (type is ModuleType mt)
            {
                b.QName = mt.qname;
            }
            else
            {
                b.QName = ExtendPath(analyzer, id);
            }

            SetIdentifierBinding(id, b);
            return b;
        }

        public void AddGlobalBinding(Analyzer analyzer, string id, Exp node, DataType type, BindingKind kind)
        {
            var b = analyzer.CreateBinding(id, node, type, kind);
            if (type is ModuleType mt)
            {
                b.QName = mt.qname;
            }
            else
            {
                b.QName = id; //ExtendPath(analyzer, id);
            }

            var globalScope = GetGlobalScope();
            Debug.Assert(globalScope == analyzer.GlobalTable);

            globalScope.SetIdentifierBinding(id, b);
        }

        public void EnsureParamBinding(Analyzer analyzer, string id, Exp node, DataType type, BindingKind kind)
        {
            var b = analyzer.GetBindingOf(node);
            if (b == null)
            {
                b = analyzer.CreateBinding(id, node, type, kind);
                if (type is ModuleType mt)
                {
                    b.QName = mt.qname;
                }
                else
                {
                    b.QName = ExtendPath(analyzer, id);
                }
            }
            else
            {
                b.Type = UnionType.Union(b.Type, type);
            }

            if (LookupLocal(id) == null)
            {
                SetIdentifierBinding(id, b);
            }
        }

        /// <summary>
        /// Directly insert a set of bindings
        /// </summary>
        public ISet<Binding> SetIdentifierBindings(string id, ISet<Binding> bs)
        {
            table[id] = bs;
            return bs;
        }

        /// <summary>
        /// Directly set a binding
        /// </summary>
        public ISet<Binding> SetIdentifierBinding(string id, Binding b)
        {
            if (table.TryGetValue(id, out var bs1))
            {
                var isBuiltin = b.Node is Url && bs1.All(x => x.Node is Url);

                if (!isBuiltin)
                {
                    throw new Exception($"identifier binding already exists: id={id}, bs1={bs1}, b={b}");
                }
            }

            ISet<Binding> bs = new HashSet<Binding> { b };
            table[id] = bs;
            return bs;
        }

        /// <summary>
        /// Look up a name in the current symbol table only. Don't recurse on the
        /// parent table.
        /// </summary>
        public ISet<Binding>? LookupLocal(string name)
        {
            return table.TryGetValue(name, out var bs) ? bs : null;
        }

        /// <summary>
        /// Look up a name in the current symbol table.  If not found,
        /// recurse on the parent symbol table.
        /// </summary>
        public ISet<Binding>? LookupBindingsOf(string name)
        {
            if (IsLocalName(name))
            {
                return LookupLocal(name);
            }

            return Parent != null ? Parent.LookupBindingsOf(name) : GetModuleBindingIfGlobal(name);

            // ISet<Binding>? b = GetModuleBindingIfGlobal(name);
            // if (b != null)
            // {
            //     return b;
            // }
            // ISet<Binding>? ent = LookupLocal(name);
            // if (ent != null)
            // {
            //     return ent;
            // }
            // else if (Parent != null)
            // {
            //     return Parent.LookupBindingsOf(name);
            // }
            // else
            // {
            //     return null;
            // }
        }

        /// <summary>
        /// Look up a name in the module if it is declared as global, otherwise look
        /// it up locally.
        /// </summary>
        public ISet<Binding>? LookupScope(string name)
        {
            var b = GetModuleBindingIfGlobal(name);
            if (b != null)
            {
                return b;
            }
            else
            {
                return LookupLocal(name);
            }
        }


        /**
         * Look up an attribute in the type hierarchy.  Don't look at parent link,
         * because the enclosing scope may not be a super class. The search is
         * "depth first, left to right" as in Python's (old) multiple inheritance
         * rule. The new MRO can be implemented, but will probably not introduce
         * much difference.
         */
        public ISet<Binding>? LookupAttribute(string attr)
        {
            return LookupAttribute(attr, new HashSet<NameScope>());
        }

        private ISet<Binding>? LookupAttribute(string attr, ISet<NameScope> visited)
        {
            if (visited.Contains(this))
            {
                return null;
            }

            var b = LookupLocal(attr);
            if (b != null)
            {
                return b;
            }

            if (superClasses != null && superClasses.Count > 0)
            {
                visited.Add(this);
                foreach (var p in superClasses)
                {
                    b = p.LookupAttribute(attr, visited);
                    if (b != null)
                    {
                        return b;
                    }
                }

                visited.Remove(this);
            }

            return null;
        }

        /// <summary>
        /// Look for a binding named <paramref name="name"/> and if found, return its type.
        /// </summary>
        public DataType? LookupTypeOf(string name)
        {
            var bs = LookupBindingsOf(name);
            if (bs is null)
            {
                return null;
            }
            else
            {
                return MakeUnion(bs);
            }
        }

        /// <summary>
        /// Look for an attribute named <param name="attr" />
        /// and if found, return its type.
        /// </summary>
        public DataType? LookupAttributeType(string attr)
        {
            var bs = LookupAttribute(attr);
            if (bs is null)
            {
                return null;
            }
            else
            {
                return MakeUnion(bs);
            }
        }

        public DataType? LookupTypeByName(string typeName)
        {
            var scope = this;
            while (scope is { })
            {
                if (scope.DataTypes.TryGetValue(typeName, out var dt))
                    return dt;
                scope = scope.Parent;
            }

            return null;
        }

        /// <summary>
        /// Create a datatype from the union of the 
        /// types of each binding.
        /// </summary>
        public static DataType MakeUnion(ISet<Binding> bs)
        {
            DataType t = DataType.Unknown;
            foreach (var b in bs)
            {
                t = UnionType.Union(t, b.Type);
            }

            return t;
        }

        /// <summary>
        /// Find a symbol table of a certain type in the enclosing scopes.
        /// </summary>
        public NameScope? GetClosestScopeOfType(NameScopeType type)
        {
            if (stateType == type)
            {
                return this;
            }
            else if (Parent is null)
            {
                return null;
            }
            else
            {
                return Parent.GetClosestScopeOfType(type);
            }
        }

        /**
         * Returns the global scope (i.e. the module scope for the current module).
         */
        public NameScope GetGlobalScope()
        {
            var scope = this;
            while (scope.Parent is not null)
            {
                scope = scope.Parent;
            }

            Debug.Assert(scope.stateType == NameScopeType.GLOBAL);
            return scope;

            // NameScope? result = GetClosestScopeOfType(NameScopeType.MODULE);
            // // Debug.Assert(result != null, "Couldn't find global table.");
            // return result;
        }


        /// <summary>
        /// If {@code name} is declared as a global, return the module binding.
        /// </summary>
        private ISet<Binding>? GetModuleBindingIfGlobal(string name)
        {
            if (!IsGlobalName(name)) return null;
            var globalScope = GetGlobalScope();
            return globalScope.LookupLocal(name);
            // if (module != this && module is not null)
            // {
            //     return module.LookupLocal(name);
            // }
        }

        public void AddAllBindings(NameScope other)
        {
            foreach (var de in other.table)
            {
                table.Add(de.Key, de.Value);
            }
        }

        public IEnumerable<ISet<Binding>> Values
        {
            get { return table.Values; }
        }

        public ICollection<KeyValuePair<string, ISet<Binding>>> Entries
        {
            get { return table; }
        }

        public string ExtendPath(Analyzer analyzer, string pathname)
        {
            var name = analyzer.ModuleName(pathname);
            return ExtendPath(Path, name);
        }

        public string ExtendPath(string? path, string name)
        {
            if (string.IsNullOrEmpty(path))
            {
                return name;
            }
            else
            {
                return path + "." + name;
            }
        }

        public override string ToString()
        {
            var parentName = Parent?.DebugName ?? Parent?.Path ?? "";
            return DebugName != null
                ? $"<State:{stateType}, {DebugName} : {parentName}>"
                : $"<State:{stateType}>, {Path} : {parentName}";
            // return "<State:" + stateType + ":" + table.Keys + ">";
        }

        /// <summary>
        /// Bind a name to this scope, including destructuring assignment.
        /// </summary>
        public void Bind(Analyzer analyzer, Exp target, DataType rvalue, BindingKind kind)
        {
            switch (target)
            {
            case Identifier id:
                Bind(analyzer, id, rvalue, kind);
                break;
            case PyTuple tup:
                Bind(analyzer, tup.Values, rvalue, kind);
                break;
            case PyList list:
                Bind(analyzer, list.Initializer, rvalue, kind);
                break;
            case AttributeAccess attr:
                var targetType = TransformExp(analyzer, attr.Expression, this);
                setAttr(analyzer, attr, rvalue, targetType);
                break;
            case ArrayRef sub:
                var valueType = TransformExp(analyzer, sub.Array, this);
                // var xform = new TypeCollector(this, analyzer);
                TransformExprs(analyzer, sub.Subs, this);
                if (valueType is ListType t)
                {
                    t.setElementType(UnionType.Union(t.eltType, rvalue));
                }

                break;
            default:
                if (target != null)
                {
                    analyzer.AddProblem(target, "invalid location for assignment");
                }

                break;
            }
        }


        /// <summary>
        /// Without specifying a kind, bind determines the kind according to the type
        /// of the scope.
        /// </summary>
        public void BindByScope(Analyzer analyzer, Exp target, DataType rvalue)
        {
            BindingKind kind;
            if (stateType == NameScopeType.FUNCTION)
            {
                kind = BindingKind.VARIABLE;
            }
            else if (stateType == NameScopeType.CLASS ||
                     stateType == NameScopeType.INSTANCE)
            {
                kind = BindingKind.ATTRIBUTE;
            }
            else
            {
                kind = BindingKind.SCOPE;
            }

            Bind(analyzer, target, rvalue, kind);
        }

        public void Bind(Analyzer analyzer, Identifier id, DataType rvalue, BindingKind kind)
        {
            if (id == null)
                return;
            // if (this.IsGlobalName(id.Name))
            // {
            //     ISet<Binding>? bs = this.LookupBindingsOf(id.Name);
            //     if (bs != null)
            //     {
            //         foreach (Binding b in bs)
            //         {
            //             b.AddType(rvalue);
            //             analyzer.AddReference(id, b);
            //         }
            //     }
            // }
            // else
            // {
            //     this.AddExpressionBinding(analyzer, id.Name, id, rvalue, kind);
            // }

            var bs = LookupBindingsOf(id.Name);

            if (bs == null)
            {
                if (IsLocalName(id.Name))
                {
                    AddLocalBinding(analyzer, id.Name, id, rvalue, kind);
                }
                else
                {
                    // TODO 验证这个逻辑：首次出现的全局变量，进行绑定
                    AddGlobalBinding(analyzer, id.Name, id, rvalue, kind);
                }
            }
            else
            {
                // TODO 验证这个逻辑：重复出现的局部变量，进行引用（原逻辑是新建绑定）
                foreach (var b in bs)
                {
                    b.AddType(rvalue);
                    analyzer.AddReference(id, b);
                }
            }
        }

        public void Bind(Analyzer analyzer, List<Exp> xs, DataType rvalue, BindingKind kind)
        {
            switch (rvalue)
            {
            case TupleType tuple:
            {
                var vs = tuple.eltTypes;
                if (xs.Count != vs.Length)
                {
                    ReportUnpackMismatch(analyzer, xs, vs.Length);
                }
                else
                {
                    for (var i = 0; i < xs.Count; i++)
                    {
                        Bind(analyzer, xs[i], vs[i], kind);
                    }
                }

                break;
            }
            case ListType list:
                Bind(analyzer, xs, list.ToTupleType(xs.Count), kind);
                break;
            case DictType dict:
                Bind(analyzer, xs, dict.ToTupleType(xs.Count), kind);
                break;
            default:
                if (rvalue.IsUnknownType())
                {
                    foreach (var x in xs)
                    {
                        Bind(analyzer, x, DataType.Unknown, kind);
                    }

                    break;
                }
                else if (xs.Count > 0)
                {
                    analyzer.AddProblem(xs[0].Filename,
                        xs[0].Start,
                        xs[xs.Count - 1].End,
                        "unpacking non-iterable: " + rvalue);
                }

                break;
            }
        }

        private static void ReportUnpackMismatch(Analyzer analyzer, List<Exp> xs, int vsize)
        {
            var xsize = xs.Count;
            var beg = xs[0].Start;
            var end = xs[xs.Count - 1].End;
            var diff = xsize - vsize;
            string msg;
            if (diff > 0)
            {
                msg = "ValueError: need more than " + vsize + " values to unpack";
            }
            else
            {
                msg = "ValueError: too many values to unpack";
            }

            analyzer.AddProblem(xs[0].Filename, beg, end, msg);
        }

        // iterator
        public void BindIterator(Analyzer analyzer, Exp target, Exp iter, DataType iterType, BindingKind kind)
        {
            if (iterType is ListType list)
            {
                Bind(analyzer, target, list.eltType, kind);
            }
            else if (iterType is TupleType tuple)
            {
                Bind(analyzer, target, tuple.ToListType().eltType, kind);
            }
            else
            {
                var ents = iterType.Scope.LookupAttribute("__iter__");
                if (ents != null)
                {
                    foreach (var ent in ents)
                    {
                        if (!(ent?.Type is FunType fnType))
                        {
                            if (!iterType.IsUnknownType())
                            {
                                analyzer.AddProblem(iter, "not an iterable type: " + iterType);
                            }

                            Bind(analyzer, target, DataType.Unknown, kind);
                        }
                        else
                        {
                            Bind(analyzer, target, fnType.GetReturnType(), kind);
                        }
                    }
                }
                else
                {
                    Bind(analyzer, target, DataType.Unknown, kind);
                }
            }
        }

        private static void setAttr(Analyzer analyzer, AttributeAccess attr, DataType attrType, DataType targetType)
        {
            if (targetType is UnionType type)
            {
                var types = type.types;
                foreach (var tp in types)
                {
                    setAttrType(analyzer, attr, tp, attrType);
                }
            }
            else
            {
                setAttrType(analyzer, attr, targetType, attrType);
            }
        }

        private static void setAttrType(Analyzer analyzer, AttributeAccess attr, DataType targetType, DataType attrType)
        {
            if (targetType.IsUnknownType())
            {
                analyzer.AddProblem(attr, "Can't set attribute for UnknownType");
                return;
            }

            var bs = targetType.Scope.LookupAttribute(attr.FieldName.Name);
            if (bs != null)
            {
                analyzer.AddRef(attr, targetType, bs);
            }

            targetType.Scope.AddLocalBinding(analyzer, attr.FieldName.Name, attr, attrType, BindingKind.ATTRIBUTE);
        }

        public static void TransformExprs(Analyzer analyzer, List<Slice> exprs, NameScope s)
        {
            var x = new TypeCollector(s, analyzer);
            foreach (var e in exprs)
            {
                e.Accept(x);
            }
        }

        public static DataType TransformExp(Analyzer analyzer, Exp n, NameScope s)
        {
            return n.Accept(new TypeCollector(s, analyzer));
        }
    }

    public enum NameScopeType
    {
        CLASS,
        INSTANCE,
        FUNCTION,
        MODULE,
        GLOBAL,
        SCOPE
    }
}