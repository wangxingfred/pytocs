using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Pytocs.Core.Types;
using Pytocs.Core.Syntax;

namespace Pytocs.Core.TypeInference
{
    /// <summary>
    /// Visits Python statements and collects data types from:
    ///  - user annotations
    ///  - expression usage.
    ///  The results are collected in the provided <see cref="State"/> object.
    /// </summary>
    public class TypeCollector :
        IStatementVisitor<DataType>,
        IExpVisitor<DataType>
    {
        private readonly NameScope scope;
        private readonly Analyzer analyzer;

        public TypeCollector(NameScope s, Analyzer analyzer)
        {
            this.scope = s;
            this.analyzer = analyzer;
        }

        private DataType Register(Exp e, DataType t)
        {
            analyzer.AddExpType(e, t);
            return t;
        }

        public DataType VisitAliasedExp(AliasedExp a)
        {
            return DataType.Unknown;
        }

        // public DataType VisitAsync(AsyncStatement a)
        // {
        //     var dt = VisitFunctionDef((FunctionDef)a.Statement, true);
        //     return dt;
        // }

        public DataType VisitAssert(AssertStatement a)
        {
            if (a.Tests != null)
            {
                foreach (var t in a.Tests)
                    t.Accept(this);
            }

            if (a.Message != null)
            {
                a.Message.Accept(this);
            }

            return DataType.Unit;
        }

        // public DataType VisitLocalVarsExp(LocalVarsExp localVarsExp)
        // {
        //     var index = 0;
        //     var initializers = localVarsExp.Initializers;
        //     foreach (var id in localVarsExp.Variables)
        //     {
        //         scope.AddLocalName(id.Name);
        //
        //         if (index < initializers.Count)
        //         {
        //             var valueType = initializers[index].Accept(this);
        //             if (valueType is EmptyTableType)
        //             {
        //                 valueType.Scope.Path += $".{id.Name}";
        //             }
        //
        //             scope.BindByScope(analyzer, id, valueType);
        //         }
        //         else
        //         {
        //             scope.BindByScope(analyzer, id, DataType.Unknown);
        //         }
        //
        //         ++index;
        //     }
        //
        //     return DataType.Unit;
        // }

        /// <summary>
        /// 判断id是否等于其文件名前缀
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private bool MatchFilename(Identifier id)
        {
            var filename = id.Filename;
            var length = filename.Length;

            var index = filename.LastIndexOf('\\');
            index = index < 0 ? 0 : index + 1;

            foreach (var c in id.Name)
            {
                if (index >= length) return false;
                if (c != filename[index]) return false;

                ++index;
            }

            return index < length && filename[index] == '.';
        }

        private void BindGlobalClass(Identifier id)
        {
            var classType = new ClassType(id.Name, scope, id.Name);

            // var moduleType = scope.DataType as ModuleType;
            // moduleType!.Class = classType;

            scope.AddGlobalBinding(analyzer, id.Name, id, classType, BindingKind.CLASS);
        }

        public DataType VisitAssignExp(AssignExp a)
        {
            // if (a.Dst is Identifier dst)
            // {
            //     var bs = scope.LookupBindingsOf(dst.Name);
            //     if (bs == null)
            //     {
            //         // TODO 是否需要找到最外层的scope？
            //         scope.AddGlobalName(dst.Name);
            //     }
            // }

            var valueType = a.Src.Accept(this);

            if (scope.stateType == NameScopeType.MODULE
                && a.Dst is Identifier id1
                && !scope.IsLocalName(id1.Name))
            {
                // 模块内的全局变量，很有可能是全局类对象
                // 例如：Boot = {}

                if (valueType is ClassType ct)
                {
                    scope.AddGlobalBinding(analyzer, id1.Name, id1, ct, BindingKind.CLASS);
                    return DataType.Unit;
                }

                if (valueType != DataType.Str
                    && valueType != DataType.Bool
                    && valueType != DataType.Int
                    && valueType != DataType.Float)
                {
                    BindGlobalClass(id1);
                    return DataType.Unit;
                }

                // 例如：TestMgr.field = 1
                // if (a.Dst is AttributeAccess attr && attr.Expression is Identifier id2
                //                                   && !scope.IsLocalName(id2.Name)
                //                                   && MatchFilename(id2))
                // {
                //     var valueType = a.Src!.Accept(this);
                //     scope.Bind(analyzer, attr, valueType, BindingKind.ATTRIBUTE);
                //     return DataType.Unit;
                // }
            }

            if (scope.stateType == NameScopeType.CLASS &&
                a.Dst is Identifier id)
            {
                /*if (id.Name == "__slots__")
                {
                    // The __slots__ attribute needs to be handled specially:
                    // it actually introduces new attributes.
                    BindClassSlots(a.Src!);
                }
                else*/
                if (a.Annotation != null)
                {
                    //$TODO: do something with the type info.
                    //var dt = scope.LookupType(a.Annotation.ToString());
                    //scope.Bind(analyzer, id, dt ?? DataType.Unknown, BindingKind.ATTRIBUTE);
                    if (a.Src != null)
                    {
                        // DataType valueType = a.Src!.Accept(this);
                        scope.BindByScope(analyzer, a.Dst, valueType);
                    }
                }
            }
            else
            {
                // var valueType = a.Src!.Accept(this);
                // if (valueType is EmptyTableType)
                // {
                //     valueType.Scope.Path += $".{a.Dst}";
                // }
                scope.BindByScope(analyzer, a.Dst, valueType);
            }

            return DataType.Unit;
        }

        // public DataType VisitAssignmentExp(AssignmentExp e)
        // {
        //     return e.Src.Accept(this);
        // }

        private void BindClassSlots(Exp eSlotNames)
        {
            IEnumerable<Exp>? slotNames = null;
            switch (eSlotNames)
            {
            case PyList srcList:
                slotNames = srcList.Initializer;
                break;
            case PyTuple srcTuple:
                slotNames = srcTuple.Values;
                break;
            case ExpList expList:
                slotNames = expList.Expressions;
                break;
            }

            if (slotNames is null)
            {
                //$TODO: dynamically generated slots are hard.
            }
            else
            {
                // Generate an attribute binding for each slot.
                foreach (var slotName in slotNames.OfType<Str>())
                {
                    var id = new Identifier(slotName.Value, slotName.Filename, slotName.Start, slotName.End);
                    scope.Bind(analyzer, id, DataType.Unknown, BindingKind.ATTRIBUTE);
                }
            }
        }

        // public DataType VisitAwait(AwaitExp e)
        // {
        //     var dt = e.Exp.Accept(this);
        //     return dt;
        // }

        public DataType VisitFieldAccess(AttributeAccess a)
        {
            // the form of ::A in ruby
            // if (a.Expression == null)
            // {
            //     return a.FieldName.Accept(this);
            // }

            var targetType = a.Expression.Accept(this);
            if (targetType is UnionType ut)
            {
                var types = ut.types;
                DataType retType = DataType.Unknown;
                foreach (var tt in types)
                {
                    retType = UnionType.Union(retType, GetAttrType(a, tt));
                }

                return retType;
            }
            else
            {
                return GetAttrType(a, targetType);
            }
        }

        public DataType GetAttrType(AttributeAccess a, DataType targetType)
        {
            var bs = targetType.Scope.LookupAttribute(a.FieldName.Name);
            if (bs is null)
            {
                analyzer.AddProblem(a.FieldName, $"attribute not found in type: {targetType}.");
                DataType t = DataType.Unknown;
                t.Scope.Path = targetType.Scope.ExtendPath(analyzer, a.FieldName.Name);
                return t;
            }
            else
            {
                analyzer.AddRef(a, targetType, bs);
                return NameScope.MakeUnion(bs);
            }
        }

        public DataType VisitBinExp(BinExp b)
        {
            var ltype = b.Left.Accept(this);
            var rtype = b.Right.Accept(this);
            if (b.Operator.IsBoolean())
            {
                return DataType.Bool;
            }
            else
            {
                return UnionType.Union(ltype, rtype);
            }
        }

        public DataType VisitSuite(SuiteStatement b)
        {
            // first pass: mark global names
            // IEnumerable<Identifier> globalNames = GetGlobalNanesInSuite(b);
            // foreach (var id in globalNames)
            // {
            //     scope.AddGlobalName(id.Name);
            //     ISet<Binding>? nb = scope.LookupBindingsOf(id.Name);
            //     if (nb is not null)
            //     {
            //         analyzer.AddReference(id, nb);
            //     }
            // }

            var returned = false;
            DataType retType = DataType.Unknown;
            foreach (var n in b.Statements)
            {
                var t = n.Accept(this);
                if (!returned)
                {
                    retType = UnionType.Union(retType, t);
                    if (!UnionType.Contains(t, DataType.Unit))
                    {
                        returned = true;
                        retType = UnionType.Remove(retType, DataType.Unit);
                    }
                }
            }

            return retType;
        }

        private static IEnumerable<Identifier> GetGlobalNanesInSuite(SuiteStatement b)
        {
            return b.Statements
                .OfType<GlobalStatement>()
                .SelectMany(g => g.Names)
                .Concat(b.Statements
                    .OfType<NonlocalStatement>()
                    .SelectMany(g => g.Names));
        }


        public DataType VisitBreak(BreakStatement b)
        {
            return DataType.None;
        }

        public DataType VisitBooleanLiteral(BooleanLiteral b)
        {
            return DataType.Bool;
        }

        /// <remarks>
        /// Most of the work here is done by the static method invoke, which is also
        /// used by Analyzer.applyUncalled. By using a static method we avoid building
        /// a NCall node for those dummy calls.
        /// </remarks>
        public DataType VisitApplication(Application c)
        {
            var fun = c.Function.Accept(this);
            var dtPos = c.Args.Select(a => a.DefaultValue!.Accept(this)).ToList();
            var hash = new Dictionary<string, DataType>();
            if (c.Keywords != null && c.Keywords.Count > 0)
            {
                foreach (var k in c.Keywords)
                {
                    hash[((Identifier) k.Name!).Name] = k.DefaultValue!.Accept(this);
                }
            }

            var dtKw = c.KwArgs?.Accept(this);
            var dtStar = c.StArgs?.Accept(this);
            if (fun is UnionType un)
            {
                DataType retType = DataType.Unknown;
                foreach (var ft in un.types)
                {
                    var t = ResolveCall(c, ft, dtPos, hash, dtKw, dtStar);
                    retType = UnionType.Union(retType, t);
                }

                return retType;
            }
            else
            {
                return ResolveCall(c, fun, dtPos, hash, dtKw, dtStar);
            }
        }

        private DataType ResolveCall(
            Application c,
            DataType fun,
            List<DataType> pos,
            IDictionary<string, DataType> hash,
            DataType? kw,
            DataType? star)
        {
            if (fun is FunType ft)
            {
                return Apply(ft, pos, hash, kw, star, c);
            }
            else if (fun is ClassType ct)
            {
                var instance = new InstanceType(ct);
                ApplyConstructor(instance, c, pos);
                return instance;
            }
            else
            {
                AddWarning(c, "calling non-function and non-class: " + fun);
                return DataType.Unknown;
            }
        }

        public void ApplyConstructor(InstanceType i, Application call, List<DataType> args)
        {
            if (i.Scope.LookupAttributeType("_Ctor") is FunType initFunc && initFunc.Definition != null)
            {
                initFunc.SelfType = i;
                Apply(initFunc, args, null, null, null, call);
                initFunc.SelfType = null;
            }
        }


        /// <summary>
        /// Called when an application of a function is encountered.
        /// </summary>
        /// <param name="funType"></param>
        /// <param name="pos">Types deduced for positional arguments.</param>
        public DataType Apply(
            FunType funType,
            List<DataType>? pos,
            IDictionary<string, DataType>? hash,
            DataType? kw,
            DataType? star,
            Exp? call)
        {
            analyzer.RemoveUncalled(funType);

            var funDef = funType.Definition;

            if (funDef is { called: false })
            {
                analyzer.CalledFunctions++;
                funDef.called = true;
            }

            if (funDef == null)
            {
                // func without definition (possibly builtins)
                return funType.GetReturnType();
            }
            else if (call != null && analyzer.InStack(call))
            {
                // Recursive call, ignore.
                funType.SelfType = null;
                return DataType.Unknown;
            }

            if (call != null)
            {
                analyzer.PushStack(call);
            }

            var pTypes = new List<DataType>();

            // Python: bind first parameter to self type
            if (funType.SelfType != null)
            {
                pTypes.Add(funType.SelfType);
            }
            // else if (funType.Class != null)
            // {
            //     pTypes.Add(funType.Class.GetInstance());
            // }

            if (pos != null)
            {
                pTypes.AddRange(pos);
            }

            BindMethodAttrs(analyzer, funType);

            var funcTable = new NameScope(funType.env, NameScopeType.FUNCTION)
            {
                Path = funType.Scope.Path
            };

            // if (funType.Scope.Parent != null && funDef.isLocal)
            // {
            //     funcTable.Path = funType.Scope.Parent.ExtendPath(analyzer, funDef.name.Name);
            // }
            // else
            // {
            //     funcTable.Path = funDef.name.Name;
            // }

            var fromType = BindParameters(
                call, funDef, funcTable,
                funDef.parameters,
                funDef.vararg,
                funDef.kwarg,
                pTypes, funType.DefaultTypes, hash, kw, star);

            if (funType.arrows.TryGetValue(fromType, out var cachedTo))
            {
                funType.SelfType = null;
                return cachedTo;
            }
            else
            {
                if (funDef.Annotation != null)
                {
                    var dtReturn = TranslateAnnotation(funDef.Annotation, funcTable);
                    funDef.body.Accept(new TypeCollector(funcTable, analyzer));
                    funType.AddMapping(fromType, dtReturn);
                    return dtReturn;
                }

                // hack: 判断IsEmpty()用于处理空函数
                var toType = funDef.body.IsEmpty()
                    ? DataType.Unit
                    : funDef.body.Accept(new TypeCollector(funcTable, analyzer));

                if (MissingReturn(toType))
                {
                    analyzer.AddProblem(funDef.name, "Function doesn't always return a value");
                    if (call != null)
                    {
                        analyzer.AddProblem(call, "Call doesn't always return a value");
                    }
                }

                toType = UnionType.Remove(toType, DataType.Unit);
                funType.AddMapping(fromType, toType);
                funType.SelfType = null;
                return toType;
            }
        }

        // public static DataType? FirstArgumentType(FunctionDef f, FunType func, DataType? selfType)
        // {
        //     if (f.parameters.Count == 0) return null;
        //     if (func.Definition!.IsStaticMethod()) return null;
        //     
        //     if (func.Definition!.IsClassMethod())
        //     {
        //         // Constructor
        //         if (func.Class != null)
        //         {
        //             return func.Class;
        //         }
        //         else if (selfType != null && selfType is InstanceType inst)
        //         {
        //             return inst.classType;
        //         }
        //     }
        //     else
        //     {
        //         // usual method
        //         if (selfType != null)
        //         {
        //             return selfType;
        //         }
        //         else if (func.Class != null)
        //         {
        //             if (func.Definition.name.Name != "__init__")
        //             {
        //                 throw new NotImplementedException("return func.Class.getInstance(null, this, call));");
        //             }
        //             else
        //             {
        //                 return func.Class.GetInstance();
        //             }
        //         }
        //     }
        //     return null;
        // }

        /// <summary>
        /// Binds the parameters of a call to the called function.
        /// </summary>
        /// <param name="analyzer"></param>
        /// <param name="call"></param>
        /// <param name="func"></param>
        /// <param name="funcTable"></param>
        /// <param name="parameters">Positional parameters as declared by the 'def'</param>
        /// <param name="rest">Rest parameters declared by the 'def'</param>
        /// <param name="restKw">Keyword parameters declared by the 'def'</param>
        /// <param name="pTypes"></param>
        /// <param name="dTypes">Default types</param>
        /// <param name="hash"></param>
        /// <param name="kw"></param>
        /// <param name="star"></param>
        /// <returns></returns>
        private DataType BindParameters(
            Node? call,
            FunctionDef func,
            NameScope funcTable,
            List<Parameter> parameters,
            Identifier? rest,
            Identifier? restKw,
            List<DataType>? pTypes,
            List<DataType>? dTypes,
            IDictionary<string, DataType>? hash,
            DataType? kw,
            DataType? star)
        {
            var fromTypes = new List<DataType>();
            var pSize = parameters == null ? 0 : parameters.Count;
            var aSize = pTypes == null ? 0 : pTypes.Count;
            var dSize = dTypes == null ? 0 : dTypes.Count;
            var nPos = pSize - dSize;

            if (star is ListType list)
            {
                star = list.ToTupleType();
            }

            // Do the positional parameters first.
            for (int i = 0, j = 0; i < pSize; i++)
            {
                var param = parameters![i];
                DataType aType;
                if (param.Annotation != null)
                {
                    aType = TranslateAnnotation(param.Annotation, funcTable);
                }
                else if (i < aSize)
                {
                    aType = pTypes![i];
                }
                else if (i - nPos >= 0 && i - nPos < dSize)
                {
                    aType = dTypes![i - nPos]!;
                }
                else
                {
                    if (hash != null && param.Id != null && hash.ContainsKey(param.Id.Name))
                    {
                        aType = hash[param.Id.Name];
                        hash.Remove(param.Id.Name);
                    }
                    else
                    {
                        if (star != null &&
                            star is TupleType tup &&
                            j < tup.eltTypes.Length)
                        {
                            aType = tup[j];
                            ++j;
                        }
                        else
                        {
                            aType = DataType.Unknown;
                            if (call != null)
                            {
                                analyzer.AddProblem(param.Id!, //$REVIEW: should be using identifiers
                                    "unable to bind argument:" + param);
                            }
                        }
                    }
                }

                // funcTable.Bind(analyzer, param.Id!, aType, BindingKind.PARAMETER);
                // TODO 这里严格来讲还应该AddLocalName，目前是修改了NameScope.IsLocalName的实现，否则LookupBindingsOf(param)会失败
                funcTable.EnsureParamBinding(analyzer, param.Id!.Name, param.Id!, aType, BindingKind.PARAMETER);
                fromTypes.Add(aType);
            }

            var fromType = analyzer.TypeFactory.CreateTuple(fromTypes.ToArray());

            if (restKw != null)
            {
                DataType dt;
                if (hash != null && hash.Count > 0)
                {
                    var hashType = UnionType.CreateUnion(hash.Values);
                    dt = analyzer.TypeFactory.CreateDict(DataType.Str, hashType);
                }
                else
                {
                    dt = DataType.Unknown;
                }

                funcTable.Bind(
                    analyzer,
                    restKw,
                    dt,
                    BindingKind.PARAMETER);
            }

            if (rest != null)
            {
                if (pTypes!.Count > pSize)
                {
                    DataType restType = analyzer.TypeFactory.CreateTuple(pTypes.SubList(pSize, pTypes.Count).ToArray());
                    funcTable.Bind(analyzer, rest, restType, BindingKind.PARAMETER);
                }
                else
                {
                    funcTable.Bind(
                        analyzer,
                        rest,
                        DataType.Unknown,
                        BindingKind.PARAMETER);
                }
            }

            return fromType;
        }

        static bool MissingReturn(DataType toType)
        {
            var hasNone = false;
            var hasOther = false;

            if (toType is UnionType ut)
            {
                foreach (var t in ut.types)
                {
                    if (t == DataType.None || t == DataType.Unit)
                    {
                        hasNone = true;
                    }
                    else
                    {
                        hasOther = true;
                    }
                }
            }

            return hasNone && hasOther;
        }

        static void BindMethodAttrs(Analyzer analyzer, FunType cl)
        {
            if (cl.Scope.Parent != null)
            {
                var cls = cl.Scope.Parent.DataType;
                if (cls != null && cls is ClassType)
                {
                    AddReadOnlyAttr(analyzer, cl, "im_class", cls, BindingKind.CLASS);
                    AddReadOnlyAttr(analyzer, cl, "__class__", cls, BindingKind.CLASS);
                    AddReadOnlyAttr(analyzer, cl, "im_self", cls, BindingKind.ATTRIBUTE);
                    AddReadOnlyAttr(analyzer, cl, "__self__", cls, BindingKind.ATTRIBUTE);
                }
            }
        }

        static void AddReadOnlyAttr(
            Analyzer analyzer,
            FunType fun,
            string name,
            DataType type,
            BindingKind kind)
        {
            Node loc = Builtins.newDataModelUrl("the-standard-type-hierarchy");
            var b = analyzer.CreateBinding(name, loc, type, kind);
            fun.Scope.SetIdentifierBinding(name, b);
            b.IsSynthetic = true;
            b.IsStatic = true;
        }

        public void AddSpecialAttribute(NameScope s, string name, DataType proptype)
        {
            var b = analyzer.CreateBinding(name, Builtins.newTutUrl("classes.html"), proptype, BindingKind.ATTRIBUTE);
            s.SetIdentifierBinding(name, b);
            b.IsSynthetic = true;
            b.IsStatic = true;
        }

        public DataType VisitClass(ClassDef c)
        {
            var path = scope.ExtendPath(analyzer, c.name.Name);
            var classType = new ClassType(c.name.Name, scope, path);
            var baseTypes = new List<DataType>();

            if (c.args != null)
            {
                foreach (var @base in c.args)
                {
                    var baseType = @base.DefaultValue!.Accept(this);
                    switch (baseType)
                    {
                    case ClassType _:
                        classType.AddSuper(baseType);
                        break;
                    case UnionType ut:
                        foreach (var parent in ut.types)
                        {
                            classType.AddSuper(parent);
                        }

                        break;
                    default:
                        analyzer.AddProblem(@base, @base + " is not a class");
                        break;
                    }

                    baseTypes.Add(baseType);
                }
            }

            // XXX: Not sure if we should add "bases", "name" and "dict" here. They
            // must be added _somewhere_ but I'm just not sure if it should be HERE.
            // AddSpecialAttribute(classType.Scope, "__bases__", analyzer.TypeFactory.CreateTuple(baseTypes.ToArray()));
            // AddSpecialAttribute(classType.Scope, "__name__", DataType.Str);
            // AddSpecialAttribute(classType.Scope, "__dict__",
                // analyzer.TypeFactory.CreateDict(DataType.Str, DataType.Unknown));
            // AddSpecialAttribute(classType.Scope, "__module__", DataType.Str);
            // AddSpecialAttribute(classType.Scope, "__doc__", DataType.Str);

            // Bind ClassType to name here before resolving the body because the
            // methods need this type as self.
            scope.Bind(analyzer, c.name, classType, BindingKind.CLASS);
            if (c.body != null)
            {
                var xform = new TypeCollector(classType.Scope, this.analyzer);
                c.body.Accept(xform);
            }

            return classType;
        }

        public DataType VisitComment(CommentStatement c)
        {
            return DataType.Unit;
        }

        public DataType VisitImaginary(ImaginaryLiteral c)
        {
            return DataType.Complex;
        }

        public DataType VisitCompFor(CompFor f)
        {
            var it = f.variable.Accept(this);
            scope.BindIterator(analyzer, f.variable, f.collection, it, BindingKind.SCOPE);
            return f.variable.Accept(this);
        }

        public DataType VisitCompIf(CompIf i)
        {
            throw new NotImplementedException();
        }

        //public DataType VisitComprehension(Comprehension c)
        //{
        //    Binder.bindIter(fs, s, c.target, c.iter, Binding.Kind.SCOPE);
        //    resolveList(c.ifs);
        //    return c.target.Accept(this);
        //}

        public DataType VisitContinue(ContinueStatement c)
        {
            return DataType.Unit;
        }

        public DataType VisitDel(DelStatement d)
        {
            foreach (var n in d.Expressions.AsList())
            {
                n.Accept(this);
                if (n is Identifier id)
                {
                    scope.Remove(id.Name);
                }
            }

            return DataType.Unit;
        }

        public DataType VisitEmptyTableExp(EmptyTableExp emptyTableExp)
        {
            var emptyTableType = new EmptyTableType
            {
                Scope =
                {
                    Path = scope.Path
                }
            };

            return emptyTableType;
        }

        public DataType VisitDictInitializer(DictInitializer d)
        {
            var keyType = ResolveUnion(d.KeyValues.Where(kv => kv.Key != null).Select(kv => kv.Key!));
            var valType = ResolveUnion(d.KeyValues.Select(kv => kv.Value));
            return analyzer.TypeFactory.CreateDict(keyType, valType);
        }

        /// 
        /// Python's list comprehension will bind the variables used in generators.
        /// This will erase the original values of the variables even after the
        /// comprehension.
        /// 
        // public DataType VisitDictComprehension(DictComprehension d)
        // {
        //     // ResolveList(d.generator);
        //     var keyType = d.Key.Accept(this);
        //     var valueType = d.Value.Accept(this);
        //     return analyzer.TypeFactory.CreateDict(keyType, valueType);
        // }
        public DataType VisitDottedName(DottedName name)
        {
            throw new NotImplementedException();
        }

        public DataType VisitEllipsis(Ellipsis e)
        {
            return DataType.None;
        }

        public DataType VisitExec(ExecStatement e)
        {
            if (e.Code != null)
            {
                e.Code.Accept(this);
            }

            if (e.Globals != null)
            {
                e.Globals.Accept(this);
            }

            if (e.Locals != null)
            {
                e.Locals.Accept(this);
            }

            return DataType.Unit;
        }

        public DataType VisitExp(ExpStatement e)
        {
            if (e.Expression != null)
            {
                e.Expression.Accept(this);
            }

            return DataType.Unit;
        }

        public DataType VisitExpList(ExpList list)
        {
            var elTypes = new List<DataType>();
            foreach (var el in list.Expressions)
            {
                var elt = el.Accept(this);
                elTypes.Add(elt);
            }

            return new TupleType(elTypes.ToArray());
        }

        public DataType VisitVariableDeclaration(VariableDeclarationStatement v)
        {
            var index = 0;
            var initializers = v.Initializers;
            foreach (var id in v.Variables)
            {
                scope.AddLocalName(id.Name);

                if (index < initializers.Count)
                {
                    var valueType = initializers[index].Accept(this);
                    if (valueType is EmptyTableType)
                    {
                        valueType.Scope.Path += $".{id.Name}";
                    }

                    scope.BindByScope(analyzer, id, valueType);
                }
                else
                {
                    scope.BindByScope(analyzer, id, DataType.Unknown);
                }

                ++index;
            }

            return DataType.Unit;
        }

        public DataType VisitVariableClassStatement(VariableClassStatement v)
        {
            var classType = v.ClassDef.Accept(this);

            scope.AddLocalBinding(analyzer, v.Variable.Name, v.Variable, classType, BindingKind.SCOPE);

            return classType;
        }

        public DataType VisitExtSlice(List<Slice> e)
        {
            foreach (var d in e)
            {
                d.Accept(this);
            }

            return new ListType();
        }

        public DataType VisitFor(ForStatement f)
        {
            scope.BindIterator(analyzer, f.Exprs, f.Tests, f.Tests.Accept(this), BindingKind.SCOPE);
            DataType ret;
            if (f.Body == null)
            {
                ret = DataType.Unknown;
            }
            else
            {
                ret = f.Body.Accept(this);
            }

            if (f.Else != null)
            {
                ret = UnionType.Union(ret, f.Else.Accept(this));
            }

            return ret;
        }

        public DataType VisitIterableUnpacker(IterableUnpacker unpacker)
        {
            var it = unpacker.Iterable.Accept(this);
            var iterType = analyzer.TypeFactory.CreateIterable(it);
            return iterType;
        }

        // public DataType VisitLambda(Lambda lambda)
        // {
        //     var env = scope.Forwarding;
        //     var fun = new FunType(lambda, env);
        //     fun.Scope.Parent = this.scope;
        //     fun.Scope.Path = scope.ExtendPath(analyzer, "{lambda}");
        //     fun.SetDefaultTypes(ResolveList(lambda.args.Select(p => p.Test!))!);
        //     analyzer.AddUncalled(fun);
        //     return fun;
        // }

        public DataType VisitFunctionDef(FunctionDef f)
            // {
            //     return VisitFunctionDef(f, false);
            // }
            //
            // public DataType VisitFunctionDef(FunctionDef f, bool isAsync)
        {
            if (f.isLocal)
            {
                scope.AddLocalName(f.name.Name);
            }

            var env = scope.Forwarding;
            var fun = new FunType(f, env)
            {
                Scope =
                {
                    Parent = scope,
                    // Path = f.isLocal ? f.name.Name : scope.ExtendPath(analyzer, f.name.Name)
                }
            };

            if (f.isLocal)
            {
                fun.Scope.Path = f.name.Name;
            }
            else if (f.cls != null)
            {
                fun.Scope.Path = scope.ExtendPath(analyzer, f.cls.Name + "." + f.name.Name);
            }
            else
            {
                fun.Scope.Path = scope.ExtendPath(analyzer, f.name.Name);
            }


            fun.SetDefaultTypes(ResolveList(f.parameters
                .Where(p => p.Test != null)
                .Select(p => p.Test!))!);

            // if (isAsync)
            // {
            //     fun = fun.MakeAwaitable();
            // }

            analyzer.AddUncalled(fun);

            if (f.cls != null) 
            {
                // 以 function A.b() 或 function A:b() 形式定义的函数，cls是A
                // 这里要做的是：定义一个类A（如果有了就找到它），然后将此函数加入到类A中
                
                var bs = scope.LookupBindingsOf(f.cls.Name);
                if (bs is { Count: > 0 })
                {
                    Debug.Assert(bs.Count == 1, $"more than one class binding for {f.cls.Name}");

                    foreach (var b in bs)
                    {
                        if (b.Type is ClassType ct)
                        {
                            fun.Class = ct;
                        }
                        else if (b.Type is InstanceType it)
                        {
                            fun.Class = it.classType;
                        }
                        else // a被认为是一个class，然而这里判断到不是，可能被识别为EmptyTable或者Dict了，需要转为class
                        {
                            fun.Class = BindingToClass(b);
                        }

                        break;
                    }
                }

                if (fun.Class != null && !fun.Class.Scope.IsLocalName(f.name.Name))
                {
                    var kind = f.IsConstructor() ? BindingKind.CONSTRUCTOR : BindingKind.METHOD;
                    fun.Class.Scope.AddLocalName(f.name.Name);
                    fun.Class.Scope.AddLocalBinding(analyzer, f.name.Name, f.name, fun, kind);

                    // todo 绑定self
                }
            }
            else
            {
                scope.Bind(analyzer, f.name, fun, BindingKind.FUNCTION);
            }

            // var ct = scope.DataType as ClassType;
            // if (ct != null)
            // {
            //     fun.Class = ct;
            // }
            // BindingKind funkind = DetermineFunctionKind(f);
            // scope.Bind(analyzer, f.name, fun, funkind);
            // var firstArgType = FirstArgumentType(f, fun, ct);
            // if (firstArgType != null)
            // {
            //     fun.Scope.Bind(analyzer, f.parameters[0].Id!, firstArgType, BindingKind.PARAMETER); // TODO 其他参数的绑定
            // }
            // else
            {
                foreach (var parameter in f.parameters)
                {
                    var id = parameter.Id;
                    if (id != null)
                    {
                        fun.Scope.AddLocalName(id.Name);
                        fun.Scope.AddLocalBinding(analyzer, id.Name, id, DataType.Unknown, BindingKind.PARAMETER);
                    }
                }
            }

            f.body.Accept(new TypeCollector(fun.Scope, this.analyzer));

            return DataType.Unit;
        }

        public DataType VisitLambda(LambdaStatement l)
        {
            throw new NotImplementedException();
        }

        private ClassType BindingToClass(Binding b)
        {
            ClassType classType;

            if (scope.stateType == NameScopeType.FUNCTION)
            {
                // 如果是在函数里面定义的类，则认为是一个闭包类，需要把闭包对象和闭包类区分开
                // 比如，下面这个定义在Animal.New中的obj闭包，需要为obj创造一个类，然后把obj的类型设置为这个类
                // function Animal.New()
                //   local obj = {}
                //   function obj.Start() end
                // end


                var className = scope.Path + "." + b.Name;

                var classBinding = scope.LookupGlobal(className);

                if (classBinding == null)
                {
                    // 类还不存在，则创建一个类，并加入到全局空间
                    classType = CreateClassType(b);

                    var clsId = new Identifier(className, b.Node.Filename, b.Node.Start, b.Node.End);
                    scope.AddGlobalBinding(analyzer, clsId.Name, clsId, classType, BindingKind.CLASS);
                }
                else if (classBinding.Type is ClassType type)
                {
                    classType = type;
                }
                else
                {
                    throw new Exception(
                        $"class binding type is not ClassType, className={className}, classBinding={classBinding}");
                }

                var instanceType = new InstanceType(classType);
                b.ChangeType(instanceType, b.Kind);
            }
            else
            {
                // 如果是在模块里面定义的类，则认为是一个普通类，直接把类型设置为ClassType
                classType = CreateClassType(b);
                b.ChangeType(classType, BindingKind.CLASS);
            }

            return classType;
        }

        /// <summary>
        /// 基于一个变量的binding创建一个类，将此变量下的成员都加入到类中
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        private ClassType CreateClassType(Binding b)
        {
            var oldType = b.Type;
            var newType = new ClassType(b.Name, scope, b.QName);

            if (oldType is DictType or EmptyTableType)
            {
                var newScope = newType.Scope;
                var oldScope = oldType.Scope;

                // TODO oldScope.Parent是null，所以暂时不处理，假定这里的scope就是定义局部类的scope
                //newScope.Parent = oldScope.Parent;

                foreach (var (k, v) in oldScope.table)
                {
                    foreach (var b1 in v)
                    {
                        b1.QName = newScope.Path + "." + b1.Name;
                        newScope.SetIdentifierBinding(b1.Name, b1);
                        newScope.AddLocalName(b1.Name);
                    }
                }
            }

            return newType;
        }

        // private BindingKind DetermineFunctionKind(FunctionDef f)
        // {
        //     if (scope.stateType == NameScopeType.CLASS)
        //     {
        //         if ("__init__" == f.name.Name)
        //         {
        //             return BindingKind.CONSTRUCTOR;
        //         }
        //         else
        //         {
        //             return BindingKind.METHOD;
        //         }
        //     }
        //     else
        //     {
        //         return BindingKind.FUNCTION;
        //     }
        // }

        public DataType VisitGlobal(GlobalStatement g)
        {
            // Do nothing here because global names are processed by VisitSuite
            return DataType.Unit;
        }

        public DataType VisitNonLocal(NonlocalStatement non)
        {
            // Do nothing here because global names are processed by VisitSuite
            return DataType.Unit;
        }

        public DataType VisitHandler(ExceptHandler h)
        {
            DataType typeval = DataType.Unknown;
            if (h.type != null)
            {
                typeval = h.type.Accept(this);
            }

            if (h.name != null)
            {
                scope.BindByScope(analyzer, h.name, typeval);
            }

            if (h.body != null)
            {
                return h.body.Accept(this);
            }
            else
            {
                return DataType.Unknown;
            }
        }

        public DataType VisitIf(IfStatement i)
        {
            DataType type1, type2;
            var s1 = scope.Clone();
            var s2 = scope.Clone();

            // Ignore condition for now
            i.Test.Accept(this);

            if (i.Then != null)
            {
                type1 = i.Then.Accept(this);
            }
            else
            {
                type1 = DataType.Unit;
            }

            if (i.Else != null)
            {
                type2 = i.Else.Accept(this);
            }
            else
            {
                type2 = DataType.Unit;
            }

            var cont1 = UnionType.Contains(type1, DataType.Unit);
            var cont2 = UnionType.Contains(type2, DataType.Unit);

            // Decide which branch affects the downstream state
            if (cont1 && cont2)
            {
                s1.Merge(s2);
                scope.Overwrite(s1);
            }
            else if (cont1)
            {
                scope.Overwrite(s1);
            }
            else if (cont2)
            {
                scope.Overwrite(s2);
            }

            return UnionType.Union(type1, type2);
        }

        public DataType VisitTest(TestExp i)
        {
            DataType type1, type2;
            i.Condition.Accept(this);

            if (i.Consequent != null)
            {
                type1 = i.Consequent.Accept(this);
            }
            else
            {
                type1 = DataType.Unit;
            }

            if (i.Alternative != null)
            {
                type2 = i.Alternative.Accept(this);
            }
            else
            {
                type2 = DataType.Unit;
            }

            return UnionType.Union(type1, type2);
        }

        // public DataType VisitImport(ImportStatement i)
        // {
        //     foreach (var a in i.Names)
        //     {
        //         DataType? mod = analyzer.LoadModule(a.Orig.segs, scope);
        //         if (mod is null)
        //         {
        //             analyzer.AddProblem(i, $"Cannot load module {string.Join(".", a.Orig.segs)}.");
        //         }
        //         else if (a.Alias != null)
        //         {
        //             scope.AddExpressionBinding(analyzer, a.Alias.Name, a.Alias, mod, BindingKind.VARIABLE);
        //         }
        //     }
        //     return DataType.Unit;
        // }

        // public DataType VisitFrom(FromStatement i)
        // {
        //     if (i.DottedName == null)
        //     {
        //         return DataType.Unit;
        //     }
        //
        //     var dtModule = analyzer.LoadModule(i.DottedName.segs, scope);
        //     if (dtModule == null)
        //     {
        //         analyzer.AddProblem(i, "Cannot load module");
        //     }
        //     else if (i.IsImportStar())
        //     {
        //         ImportStar(i, dtModule);
        //     }
        //     else
        //     {
        //         foreach (var a in i.AliasedNames)
        //         {
        //             Identifier idFirst = a.Orig.segs[0];
        //             var bs = dtModule.Scope.LookupBindingsOf(idFirst.Name);
        //             if (bs != null)
        //             {
        //                 if (a.Alias != null)
        //                 {
        //                     scope.SetIdentifierBindings(a.Alias.Name, bs);
        //                     analyzer.AddReference(a.Alias, bs);
        //                 }
        //                 else
        //                 {
        //                     scope.SetIdentifierBindings(idFirst.Name, bs);
        //                     analyzer.AddReference(idFirst, bs);
        //                 }
        //             }
        //             else
        //             {
        //                 var ext = new List<Identifier>(i.DottedName.segs)
        //                 {
        //                     idFirst
        //                 };
        //                 DataType? mod2 = analyzer.LoadModule(ext, scope);
        //                 if (mod2 != null)
        //                 {
        //                     if (a.Alias != null)
        //                     {
        //                         scope.AddExpressionBinding(analyzer, a.Alias.Name, a.Alias, mod2, BindingKind.VARIABLE);
        //                     }
        //                     else
        //                     {
        //                         scope.AddExpressionBinding(analyzer, idFirst.Name, idFirst, mod2, BindingKind.VARIABLE);
        //                     }
        //                 }
        //             }
        //         }
        //     }
        //     return DataType.Unit;
        // }

        /// <summary>
        /// Import all names from a module.
        /// </summary>
        /// <param name="i">Import statement</param>
        /// <param name="mt">Module being imported</param>
        // private void ImportStar(FromStatement i, DataType? mt)
        // {
        //     if (mt is null || mt.file == null)
        //     {
        //         return;
        //     }
        //
        //     Module? node = analyzer.GetAstForFile(mt.file);
        //     if (node == null)
        //     {
        //         return;
        //     }
        //
        //     DataType? allType = mt.Scope.LookupTypeOf("__all__");
        //     List<string> names = new List<string>();
        //     if (allType is ListType lt)
        //     {
        //         foreach (var s in lt.values.OfType<string>()) 
        //         {
        //             names.Add(s);
        //         }
        //     }
        //
        //     if (names.Count > 0)
        //     {
        //         int start = i.Start;
        //         foreach (string name in names)
        //         {
        //             ISet<Binding>? b = mt.Scope.LookupLocal(name);
        //             if (b != null)
        //             {
        //                 scope.SetIdentifierBindings(name, b);
        //             }
        //             else
        //             {
        //                 var m2 = new List<Identifier>(i.DottedName!.segs);
        //                 var fakeName = new Identifier(name, i.Filename, start, start + name.Length);
        //                 m2.Add(fakeName);
        //                 DataType? type = analyzer.LoadModule(m2, scope);
        //                 if (type is not null)
        //                 {
        //                     start += name.Length;
        //                     scope.AddExpressionBinding(analyzer, name, fakeName, type, BindingKind.VARIABLE);
        //                 }
        //             }
        //         }
        //     }
        //     else
        //     {
        //         // Fall back to importing all names not starting with "_".
        //         foreach (var e in mt.Scope.Entries.Where(en => !en.Key.StartsWith("_")))
        //         {
        //             scope.SetIdentifierBindings(e.Key, e.Value);
        //         }
        //     }
        // }
        public DataType? VisitArgument(Argument arg)
        {
            return arg.DefaultValue?.Accept(this);
        }

        public DataType VisitBigLiteral(BigLiteral i)
        {
            return Register(i, DataType.Int);
        }

        public DataType VisitIntLiteral(IntLiteral i)
        {
            return DataType.Int;
        }

        public DataType VisitList(PyList l)
        {
            var listType = new ListType();
            if (l.Initializer.Count == 0)
                return Register(l, listType); // list of unknown.
            foreach (var exp in l.Initializer)
            {
                listType.Add(exp.Accept(this));
                if (exp is Str sExp)
                {
                    listType.addValue(sExp.Value);
                }
            }

            return Register(l, listType);
        }

        /// <remarks>
        /// Python's list comprehension will bind the variables used in generators.
        /// This will erase the original values of the variables even after the
        /// comprehension.
        /// </remarks>
        public DataType VisitListComprehension(ListComprehension l)
        {
            l.Collection.Accept(this);
            return analyzer.TypeFactory.CreateList(l.Collection.Accept(this));
        }

        /// 
        /// Python's list comprehension will erase any variable used in generators.
        /// This is wrong, but we "respect" this bug here.
        /// 
        public DataType VisitGeneratorExp(GeneratorExp g)
        {
            g.Collection.Accept(this);
            return analyzer.TypeFactory.CreateList(g.Projection.Accept(this));
        }

        public DataType VisitLongLiteral(LongLiteral l)
        {
            return DataType.Int;
        }

        public ModuleType VisitModule(Module m)
        {
            string? qname = null;
            if (m.Filename != null)
            {
                // This will return null iff specified file is not prefixed by
                // any path in the module search path -- i.e., the caller asked
                // the analyzer to load a file not in the search path.
                qname = analyzer.GetModuleQname(m.Filename);
            }

            if (qname == null)
            {
                qname = m.Name;
            }

            var mt = analyzer.TypeFactory.CreateModule(m.Name, m.Filename!, qname, analyzer.GlobalTable);

            scope.AddModuleBinding(analyzer, analyzer.GetModuleQname(m.Filename!), m, mt, BindingKind.MODULE);
            if (m.Body != null)
            {
                m.Body.Accept(new TypeCollector(mt.Scope, this.analyzer));
            }

            return mt;
        }

        public DataType VisitIdentifier(Identifier id)
        {
            var b = scope.LookupBindingsOf(id.Name);
            if (b != null)
            {
                analyzer.AddReference(id, b);
                analyzer.Resolved.Add(id);
                analyzer.Unresolved.Remove(id);
                return NameScope.MakeUnion(b);
            }
            else if (id.Name == "true" || id.Name == "false")
            {
                return DataType.Bool;
            }
            else
            {
                analyzer.AddProblem(id, $"unbound variable {id.Name}");
                analyzer.Unresolved.Add(id);
                DataType t = DataType.Unknown;
                t.Scope.Path = scope.ExtendPath(analyzer, id.Name);
                return t;
            }
        }

        public DataType VisitNoneExp()
        {
            return DataType.None;
        }

        // public DataType VisitPass(PassStatement p)
        // {
        //     return DataType.Unit;
        // }

        public DataType VisitPrint(PrintStatement p)
        {
            if (p.OutputStream != null)
            {
                p.OutputStream.Accept(this);
            }

            if (p.Args != null)
            {
                ResolveList(p.Args.Select(a => a.DefaultValue!));
            }

            return DataType.Unit;
        }

        // public DataType VisitRaise(RaiseStatement r)
        // {
        //     if (r.ExToRaise != null)
        //     {
        //         r.ExToRaise.Accept(this);
        //     }
        //     if (r.ExOriginal != null)
        //     {
        //         r.ExOriginal.Accept(this);
        //     }
        //     if (r.Traceback != null)
        //     {
        //         r.Traceback.Accept(this);
        //     }
        //     return DataType.Unit;
        // }

        public DataType VisitRealLiteral(RealLiteral r)
        {
            return DataType.Float;
        }

        //public DataType VisitRepr(Repr r)
        //{
        //    if (r.value != null)
        //    {
        //        r.value.Accept(this);
        //    }
        //    return DataType.STR;
        //}

        public DataType VisitReturn(ReturnStatement r)
        {
            if (r.Expression == null)
            {
                return DataType.None;
            }
            else
            {
                return r.Expression.Accept(this);
            }
        }

        public DataType VisitSet(PySet s)
        {
            var valType = ResolveUnion(s.Initializer);
            return new SetType(valType);
        }

        public DataType VisitSetComprehension(SetComprehension s)
        {
            var items = s.Collection.Accept(this);
            return new SetType(items);
        }

        public DataType VisitSlice(Slice s)
        {
            if (s.Lower != null)
            {
                s.Lower.Accept(this);
            }

            if (s.Upper != null)
            {
                s.Upper.Accept(this);
            }

            if (s.Stride != null)
            {
                s.Stride.Accept(this);
            }

            return analyzer.TypeFactory.CreateList();
        }

        public DataType VisitStarExp(StarExp s)
        {
            return s.Expression.Accept(this);
        }

        public DataType VisitStr(Str s)
        {
            return DataType.Str;
        }

        public DataType VisitBytes(Bytes b)
        {
            return DataType.Str;
        }

        public DataType VisitArrayRef(ArrayRef s)
        {
            var vt = s.Array.Accept(this);
            DataType? st;
            if (s.Subs == null || s.Subs.Count == 0)
                st = null;
            else if (s.Subs.Count == 1)
                st = s.Subs[0].Accept(this);
            else
                st = VisitExtSlice(s.Subs);

            if (vt is UnionType ut)
            {
                DataType retType = DataType.Unknown;
                foreach (var t in ut.types)
                {
                    retType = UnionType.Union(retType, GetSubscript(s, t, st));
                }

                return retType;
            }
            else
            {
                return GetSubscript(s, vt, st);
            }
        }

        public DataType GetSubscript(ArrayRef s, DataType vt, DataType? st)
        {
            if (vt.IsUnknownType())
            {
                return DataType.Unknown;
            }

            switch (vt)
            {
            case ListType list:
                return GetListSubscript(s, list, st);
            case TupleType tup:
                return GetListSubscript(s, tup.ToListType(), st);
            case DictType dt:
                if (!dt.KeyType.Equals(st!))
                {
                    AddWarning(s, "Possible KeyError (wrong type for subscript)");
                }

                return dt.ValueType;
            case StrType _:
                if (st != null && (st is ListType || st.IsNumType()))
                {
                    return vt;
                }
                else
                {
                    AddWarning(s, "Possible KeyError (wrong type for subscript)");
                    return DataType.Unknown;
                }
            default:
                return DataType.Unknown;
            }
        }

        private DataType GetListSubscript(ArrayRef s, DataType vt, DataType? st)
        {
            if (vt is ListType list)
            {
                if (st is ListType)
                {
                    return vt;
                }
                else if (st is null || st.IsNumType())
                {
                    return list.eltType;
                }
                else
                {
                    var sliceFunc = vt.Scope.LookupAttributeType("__getslice__");
                    if (sliceFunc is null)
                    {
                        AddError(s, "The type can't be sliced: " + vt);
                        return DataType.Unknown;
                    }
                    else if (sliceFunc is FunType ft)
                    {
                        return Apply(ft, null, null, null, null, s);
                    }
                    else
                    {
                        AddError(s, "The type's __getslice__ method is not a function: " + sliceFunc);
                        return DataType.Unknown;
                    }
                }
            }
            else
            {
                return DataType.Unknown;
            }
        }

        public DataType VisitTry(TryStatement t)
        {
            DataType tp1 = DataType.Unknown;
            DataType tp2 = DataType.Unknown;
            DataType tph = DataType.Unknown;
            DataType tpFinal = DataType.Unknown;

            if (t.ExHandlers != null)
            {
                foreach (var h in t.ExHandlers)
                {
                    tph = UnionType.Union(tph, VisitHandler(h));
                }
            }

            if (t.Body != null)
            {
                tp1 = t.Body.Accept(this);
            }

            if (t.ElseHandler != null)
            {
                tp2 = t.ElseHandler.Accept(this);
            }

            if (t.FinallyHandler != null)
            {
                tpFinal = t.FinallyHandler.Accept(this);
            }

            return new UnionType(tp1, tp2, tph, tpFinal);
        }

        public DataType VisitTuple(PyTuple t)
        {
            var tts = t.Values.Select(e => e.Accept(this)).ToArray();
            var tt = analyzer.TypeFactory.CreateTuple(tts);
            return tt;
        }

        public DataType VisitUnary(UnaryExp u)
        {
            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (u.Operator == Op.LuaLength)
            {
                return DataType.Int;
            }

            return u.Exp.Accept(this);
        }

        public DataType VisitUrl(Url s)
        {
            return DataType.Str;
        }

        public DataType VisitWhile(WhileStatement w)
        {
            w.Test.Accept(this);
            DataType t = DataType.Unknown;

            if (w.Body != null)
            {
                t = w.Body.Accept(this);
            }

            if (w.Else != null)
            {
                t = UnionType.Union(t, w.Else.Accept(this));
            }

            return t;
        }

        public DataType VisitWith(WithStatement w)
        {
            foreach (var item in w.Items)
            {
                var val = item.t.Accept(this);
                if (item.e != null)
                {
                    scope.BindByScope(analyzer, item.e, val);
                }
            }

            return w.Body.Accept(this);
        }

        // public DataType VisitYieldExp(YieldExp y)
        // {
        //     if (y.Expression != null)
        //     {
        //         return analyzer.TypeFactory.CreateList(y.Expression.Accept(this));
        //     }
        //     else
        //     {
        //         return DataType.None;
        //     }
        // }
        //
        // public DataType VisitYieldFromExp(YieldFromExp y)
        // {
        //     if (y.Expression != null)
        //     {
        //         return analyzer.TypeFactory.CreateList(y.Expression.Accept(this));
        //     }
        //     else
        //     {
        //         return DataType.None;
        //     }
        // }

        public DataType VisitYield(YieldStatement y)
        {
            if (y.Expression != null)
                return analyzer.TypeFactory.CreateList(y.Expression.Accept(this));
            else
                return DataType.None;
        }

        protected void AddError(Node n, string msg)
        {
            analyzer.AddProblem(n, msg);
        }

        protected void AddWarning(Node n, string msg)
        {
            analyzer.AddProblem(n, msg);
        }

        /// <summary>
        /// Resolves each element, and constructs a result list.
        /// </summary>
        private List<DataType?>? ResolveList(IEnumerable<Exp?>? nodes)
        {
            if (nodes is null)
            {
                return null;
            }
            else
            {
                return nodes
                    .Select(n => n?.Accept(this))
                    .ToList();
            }
        }

        /// <summary>
        /// Utility method to resolve every node in <param name="nodes"/> and
        /// return the union of their types.  If <param name="nodes" /> is empty or
        /// null, returns a new {@link Pytocs.Core.Types.UnknownType}.
        /// </summary>
        public DataType ResolveUnion(IEnumerable<Exp> nodes)
        {
            DataType result = DataType.Unknown;
            foreach (var node in nodes)
            {
                var nodeType = node.Accept(this);
                result = UnionType.Union(result, nodeType);
            }

            return result;
        }

        /// <summary>
        /// Translate a Python type annotation to a <see cref="DataType"/>.
        /// </summary>
        private DataType TranslateAnnotation(Exp exp, NameScope scope)
        {
            switch (exp)
            {
            case ArrayRef aref:
            {
                // Generic types are expressed like: GenericType[TypeArg1,TypeArg2,...]
                var dts = new List<DataType>();
                foreach (var sub in aref.Subs)
                {
                    if (sub.Lower != null)
                    {
                        dts.Add(TranslateAnnotation(sub.Lower, scope));
                    }
                    else
                    {
                        dts.Add(DataType.Unknown);
                    }
                }

                var genericType = scope.LookupTypeByName(aref.Array.ToString());
                if (genericType is null)
                {
                    analyzer.AddProblem(exp, string.Format(Resources2.ErrUnknownTypeInTypeAnnotation, exp));
                    return DataType.Unknown;
                }

                return genericType.MakeGenericType(dts.ToArray());
            }
            case Identifier id:
            {
                var dt = scope.LookupTypeByName(id.Name);
                if (dt is null)
                {
                    analyzer.AddProblem(exp, string.Format(Resources2.ErrUnknownTypeInTypeAnnotation, exp));
                    return DataType.Unknown;
                }

                return dt;
            }
            default:
                analyzer.AddProblem(exp, string.Format(Resources2.ErrUnknownTypeInTypeAnnotation, exp));
                return DataType.Unknown;
            }
        }
    }

    public static class ListEx
    {
        public static List<T> SubList<T>(this List<T> list, int iMin, int iMac)
        {
            return list.Skip(iMin).Take(iMac - iMin).ToList();
        }
    }
}