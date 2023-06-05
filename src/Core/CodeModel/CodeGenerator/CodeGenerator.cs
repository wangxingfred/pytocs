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

using Pytocs.Core.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pytocs.Core.CodeModel
{
    /// <summary>
    /// Factory class that can be called to build up C# code.
    /// </summary>
    public class CodeGenerator
    {
        private CSharpCodeProvider provider;
        private CodeCompileUnit unt;
        private bool isInit;

        public CodeGenerator(CodeCompileUnit unt, string modulePath, string moduleName)
        {
            this.unt = unt;
            this.isInit = moduleName == "__init__";
            this.provider = new CSharpCodeProvider();
            this.Scope = new List<CodeStatement>(); // dummy scope.
            this.CurrentNamespace = new CodeNamespace(modulePath);
            this.CurrentType = new CodeTypeDeclaration(moduleName)
            {
                IsClass = true,
                Attributes = /*MemberAttributes.Static | */MemberAttributes.Public
            };
            CurrentNamespace.Types.Add(CurrentType);
            unt.Namespaces.Add(CurrentNamespace);

            CurrentStatements = Scope;
        }


        public List<CodeStatement> Scope { get; private set; }
        public CodeMember? CurrentMember { get; private set; }
        public List<CodeStatement>? CurrentStatements { get; private set; }
        public List<CodeCommentStatement>? CurrentComments { get; private set; }
        public CodeNamespace CurrentNamespace { get; set; }
        public CodeTypeDeclaration CurrentType { get; set; }
        
        public const string Singleton = "Singleton";


        public CodeExpression Access(CodeExpression exp, string fieldName)
        {
            return new CodeFieldReferenceExpression(exp, fieldName);
        }

        public CodeExpression AccessSingletonField(CodeExpression exp, string fieldName)
        {
            exp = Access(exp, Singleton);
            return new CodeFieldReferenceExpression(exp, fieldName);
        }

        public CodeExpression AssignExp(CodeExpression id, CodeExpression exp)
        {
            return new CodeBinaryOperatorExpression(id, CodeOperatorType.Assign, exp);
        }

        public CodeBinaryOperatorExpression BinOp(CodeExpression l, CodeOperatorType op, CodeExpression r)
        {
            return new CodeBinaryOperatorExpression(l, op, r);
        }

        public CodeCatchClause CatchClause(string? localName, CodeTypeReference? type, Action generateClauseBody)
        {
            var clause = new CodeCatchClause(localName, type);
            var oldScope = Scope;
            Scope = clause.Statements;
            generateClauseBody();
            Scope = oldScope;
            return clause;
        }

        public CodeTypeDeclaration Enum(
            string name,
            Func<IEnumerable<CodeMemberField>> valueGenerator)
        {
            var c = new CodeTypeDeclaration
            {
                IsEnum = true,
                Name = name,
            };
            // classes in __init__ files go directly into the namespace.
            if (this.isInit)
            {
                CurrentNamespace.Types.Add(c);
            }
            else
            {
                AddMemberWithComments(c);
            }

            c.Members.AddRange(valueGenerator());
            return c;
        }

        public CodeTypeDeclaration Class(
            string name,
            IEnumerable<string>? baseClasses,
            Func<IEnumerable<CodeMemberField>> fieldGenerator,
            Action bodyGenerator)
        {
            var baseTypes = baseClasses?.Select(b => new CodeTypeReference(b)).ToArray();
            
            if (name == CurrentType.Name)
            {
                // 用于模块中定义同名类的情况
                // TODO: 或许应该通过name去查找是否已经存在同名类？

                if (baseTypes != null) CurrentType.BaseTypes.AddRange(baseTypes);

                // TODO CurrentType.Members.AddRange(fieldGenerator());
                return CurrentType;
            }

            var oldScope = Scope;
            Scope = new List<CodeStatement>();
            
            var c = new CodeTypeDeclaration
            {
                IsClass = true,
                Name = name,
            };

            // classes in __init__ files go directly into the namespace.
            if (this.isInit)
            {
                CurrentNamespace.Types.Add(c);
            }
            else
            {
                AddMemberWithComments(c);
            }

            if (baseTypes != null) CurrentType.BaseTypes.AddRange(baseTypes);
            var old = CurrentType;
            var oldMethod = CurrentMember;
            var oldStmts = CurrentStatements;
            var oldComments = CurrentComments;
            var oldIsInit = isInit;
            CurrentType = c;
            CurrentMember = null;
            CurrentStatements = null;
            CurrentComments = null;
            isInit = false;
            c.Members.AddRange(fieldGenerator());
            bodyGenerator();
            CurrentMember = oldMethod;
            CurrentStatements = oldStmts;
            CurrentComments = oldComments;

            CurrentType = old;
            isInit = oldIsInit;
            Scope = oldScope;
            return c;
        }

        public CodeTypeDeclaration? FindTypeDeclaration(string name)
        {
            return CurrentNamespace.Types.LastOrDefault(t => t.Name == name);
        }

        private void AddMemberWithComments(CodeMember c, CodeTypeDeclaration? type = null)
        {
            c.Comments.AddRange(Scope.OfType<CodeCommentStatement>());
            Scope.RemoveAll(s => s is CodeCommentStatement);
            
            type ??= CurrentType;
            type.Members.Add(c);
        }

        public bool HasMember(string name)
        {
            return CurrentType.Members.Any(m => m.Name == name);
        }

        public CodeMember? FindMember(string className, string memberName)
        {
            var c = FindTypeDeclaration(className);
            return c?.Members.FirstOrDefault(m => m.Name == memberName);
        }

        public CodeAssignStatement Assign(CodeExpression lhs, CodeExpression rhs)
        {
            var ass = new CodeAssignStatement(lhs, rhs);
            Scope.Add(ass);
            return ass;
        }

        public CodeAssignStatement Assign(CodeExpression lhs, string field, CodeExpression rhs)
        {
            var access = new CodeFieldReferenceExpression(lhs, field);
            var ass = new CodeAssignStatement(access, rhs);
            Scope.Add(ass);
            return ass;
        }

        public CodeConditionStatement If(CodeExpression test, Action xlatThen)
        {
            var i = new CodeConditionStatement
            {
                Condition = test
            };
            Scope.Add(i);
            var old = Scope;
            Scope = i.TrueStatements;
            xlatThen();
            Scope = old;
            return i;
        }

        public CodeConditionStatement If(CodeExpression test, Action xlatThen, Action xlatElse)
        {
            var i = new CodeConditionStatement
            {
                Condition = test
            };
            Scope.Add(i);
            var old = Scope;
            Scope = i.TrueStatements;
            xlatThen();
            Scope = i.FalseStatements;
            xlatElse();
            Scope = old;
            return i;
        }

        public CodeStatement Foreach(CodeExpression exp, CodeExpression list, Action xlatLoopBody)
        {
            var c = new CodeForeachStatement(exp, list);
            Scope.Add(c);
            var old = Scope;
            Scope = c.Statements;
            xlatLoopBody();
            Scope = old;
            return c;
        }

        /// <summary>
        /// 遍历集合的索引和元素
        /// </summary>
        /// <param name="varIndex"></param>
        /// <param name="varItem"></param>
        /// <param name="collection"></param>
        /// <param name="xlatLoopBody"></param>
        /// <returns></returns>
        public CodeStatement ForIndexItem(CodeVariableReferenceExpression varIndex,
            CodeVariableReferenceExpression? varItem, CodeExpression collection, Action xlatLoopBody)
        {
            var initializer = CodeVariableDeclarationStatement.CreateVar(varIndex.Name, Number(1));

            var condition = new CodeBinaryOperatorExpression(varIndex, CodeOperatorType.Le,
                new CodeFieldReferenceExpression(collection, "Count"));

            var increment = new CodeAssignStatement(varIndex,
                new CodeBinaryOperatorExpression(varIndex, CodeOperatorType.Add, Number(1)));

            var c = new CodeForStatement(initializer, condition, increment);

            Scope.Add(c);
            var old = Scope;
            Scope = c.Statements;

            if (varItem != null)
            {
                var declarItem = CodeVariableDeclarationStatement.CreateVar(varItem.Name,
                    new CodeArrayIndexerExpression(collection, varIndex));
                Scope.Add(declarItem);
            }

            xlatLoopBody();
            Scope = old;
            return c;
        }

        /// <summary>
        /// 遍历某个范围的整数
        /// </summary>
        /// <param name="variable"></param>
        /// <param name="initValue"></param>
        /// <param name="condition"></param>
        /// <param name="incValue"></param>
        /// <param name="xlatLoopBody"></param>
        /// <returns></returns>
        public CodeStatement ForRange(CodeVariableReferenceExpression variable, CodeExpression initValue,
            CodeExpression condition, CodeExpression incValue, Action xlatLoopBody)
        {
            var initializer = CodeVariableDeclarationStatement.CreateVar(variable.Name, initValue);

            var increment = new CodeAssignStatement(variable,
                new CodeBinaryOperatorExpression(variable, CodeOperatorType.Add, incValue));

            var c = new CodeForStatement(initializer, condition, increment);

            Scope.Add(c);
            var old = Scope;
            Scope = c.Statements;
            xlatLoopBody();
            Scope = old;
            return c;
        }

        public CodeExpression Appl(CodeExpression fn, params CodeExpression[] args)
        {
            return new CodeApplicationExpression(fn, args);
        }

        public CodeExpression Sub(CodeExpression minuend, CodeExpression subtrahend)
        {
            return new CodeBinaryOperatorExpression(minuend, CodeOperatorType.Sub, subtrahend);
        }

        public CodeExpression Add(CodeExpression augend, CodeExpression addend)
        {
            return new CodeBinaryOperatorExpression(augend, CodeOperatorType.Add, addend);
        }

        public CodeExpression Div(CodeExpression dividend, CodeExpression divisor)
        {
            return new CodeBinaryOperatorExpression(dividend, CodeOperatorType.Div, divisor);
        }

        public CodeExpression Mul(CodeExpression multiplicand, CodeExpression multiplier)
        {
            return new CodeBinaryOperatorExpression(multiplicand, CodeOperatorType.Mul, multiplier);
        }

        public CodeExpression ApplyMethod(CodeExpression obj, string method, params CodeExpression[] args)
        {
            return new CodeApplicationExpression(
                this.MethodRef(obj, method),
                args);
        }

        public void SetCurrentFunction(ICodeFunction fn)
        {
            if (fn is CodeMember member) CurrentMember = member;
            CurrentStatements = fn.Statements;
            CurrentComments = fn.Comments;
            Scope = fn.Statements!;
        }

        public void SetCurrentPropertyAccessor(
            CodeMemberProperty property,
            List<CodeStatement> stmts)
        {
            this.CurrentMember = property;
            this.CurrentStatements = stmts;
            this.CurrentComments = property.Comments;
        }

        public CodeStatement SideEffect(CodeExpression exp)
        {
            var sideeffect = new CodeExpressionStatement(exp);
            Scope.Add(sideeffect);
            return sideeffect;
        }

        public CodeConstructor Constructor(IEnumerable<CodeParameterDeclarationExpression> parms, Action body)
        {
            var cons = new CodeConstructor
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final
            };
            cons.Parameters.AddRange(parms.ToArray());
            AddMemberWithComments(cons);

            GenerateMethodBody(cons, body);
            return cons;
        }

        public CodeMemberMethod Method(
            string name,
            IEnumerable<CodeParameterDeclarationExpression> parms,
            CodeTypeReference retType,
            Action body)
        {
            var method = new CodeMemberMethod
            {
                Name = name,
                Attributes = MemberAttributes.Public,
                ReturnType = retType
            };
            method.Parameters.AddRange(parms.ToArray());
            AddMemberWithComments(method);

            GenerateMethodBody(method, body);
            return method;
        }

        public CodeLocalFunction LocalFunction(
            string name,
            CodeTypeReference retType,
            IEnumerable<CodeParameterDeclarationExpression> parms,
            Action body)
        {
            var localFn = new CodeLocalFunction
            {
                Name = name,
                ReturnType = retType,
            };
            localFn.Parameters.AddRange(parms);

            GenerateMethodBody(localFn, body);
            return localFn;
        }

        public CodeMemberMethod StaticMethod(
            string name,
            IEnumerable<CodeParameterDeclarationExpression> parms,
            CodeTypeReference? retType,
            Action body)
        {
            var method = new CodeMemberMethod
            {
                Name = name,
                Attributes = MemberAttributes.Public | MemberAttributes.Static,
                ReturnType = retType,
            };
            method.Parameters.AddRange(parms.ToArray());
            AddMemberWithComments(method);

            GenerateMethodBody(method, body);
            return method;
        }

        public CodeLambdaStatement LambdaMethod(CodeVariableDeclarationStatement declaration,
            IEnumerable<CodeParameterDeclarationExpression> parameters, Action body)
        {
            var codeLambda = new CodeLambdaStatement(declaration);
            codeLambda.Parameters.AddRange(parameters);
            GenerateMethodBody(codeLambda, body);
            return codeLambda;
        }

        private void GenerateMethodBody(ICodeFunction fn, Action body)
        {
            var old = Scope;
            var oldMethod = CurrentMember;
            var oldStatements = CurrentStatements;
            var oldComments = CurrentComments;
            SetCurrentFunction(fn);
            body();
            Scope = old;
            CurrentMember = oldMethod;
            CurrentStatements = oldStatements;
            CurrentComments = oldComments;
        }

        public CodeParameterDeclarationExpression Param(Type type, string name)
        {
            return new CodeParameterDeclarationExpression(type, name);
        }

        public CodeParameterDeclarationExpression Param(Type type, string name, CodeExpression defaultValue)
        {
            return new CodeParameterDeclarationExpression(type, name, defaultValue);
        }

        public void Return(CodeExpression? e = null)
        {
            Scope.Add(new CodeMethodReturnStatement(e));
        }

        public void Using(string @namespace)
        {
            CurrentNamespace.Imports.Add(new CodeNamespaceImport(@namespace));
        }

        public void Using(string alias, string @namespace)
        {
            CurrentNamespace.Imports.Add(new CodeNamespaceImport(
                EscapeKeywordName(alias) +
                " = " +
                EscapeKeywordName(@namespace)));
        }

        public string EscapeKeywordName(string name)
        {
            return IndentingTextWriter.NameNeedsQuoting(name)
                ? "@" + name
                : name;
        }

        public CodeMemberField Field(CodeTypeReference fieldType, string fieldName)
        {
            var field = new CodeMemberField(fieldType, fieldName)
            {
                Attributes = MemberAttributes.Public,
            };
            AddMemberWithComments(field);
            return field;
        }
        
        public CodeMemberField Field(CodeTypeReference fieldType, string fieldName,
            CodeExpression? initializer, string? className = null)
        {
            var field = new CodeMemberField(fieldType, fieldName)
            {
                Attributes = MemberAttributes.Public,
                InitExpression = initializer,
            };

            var classTypeDecl = className != null ? FindTypeDeclaration(className) : null;
            AddMemberWithComments(field, classTypeDecl);

            return field;
        }

        // public CodeMemberField Field(string fieldName, CodeExpression initializer)
        // {
        //     var field = new CodeMemberField(typeof(object), fieldName)
        //     {
        //         Attributes = MemberAttributes.Public,
        //         InitExpression = initializer,
        //     };
        //     AddMemberWithComments(field);
        //     return field;
        // }

        public CodeMemberField FieldStaticSingleton(CodeTypeReference typeReference)
        {
            var initializer = New(typeReference);
            var field = new CodeMemberField(typeReference, Singleton)
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Static,
                InitExpression = initializer,
            };
            AddMemberWithComments(field);
            return field;
        }

        public CodeArrayIndexerExpression Aref(CodeExpression exp, CodeExpression[] indices)
        {
            return new CodeArrayIndexerExpression(exp, indices);
        }

        // public CodeAwaitExpression Await(CodeExpression exp)
        // {
        //     return new CodeAwaitExpression(exp);
        // }


        public CodeThrowExceptionStatement Throw(CodeExpression codeExpression)
        {
            var t = new CodeThrowExceptionStatement(codeExpression);
            Scope.Add(t);
            return t;
        }

        public CodeThrowExceptionStatement Throw()
        {
            var t = new CodeThrowExceptionStatement();
            Scope.Add(t);
            return t;
        }

        public CodeExpression Lambda(CodeExpression[] args, CodeExpression expr)
        {
            return new CodeLambdaExpression(args, expr);
        }

        public CodeExpression Lambda(CodeExpression[] args, List<CodeStatement> stmts)
        {
            return new CodeLambdaExpression(args, stmts);
        }

        public CodeExpression ListInitializer(CodeTypeReference elemType, IEnumerable<CodeExpression>? exprs)
        {
            // EnsureImport(Translate.TypeReferenceTranslator.GenericCollectionNamespace);
            EnsureImport(Translate.TypeReferenceTranslator.CoreNamespace);
            var list = new CodeObjectCreateExpression
            {
                Type = new CodeTypeReference("LuaList", elemType)
            };
            if (exprs != null) list.Initializers!.AddRange(exprs);
            return list;
        }

        public CodeExpression Base()
        {
            return new CodeBaseReferenceExpression();
        }

        public void EnsureImport(string nmespace)
        {
            if (CurrentNamespace.Imports.Where(i => i.Namespace == nmespace).Any())
                return;
            CurrentNamespace.Imports.Add(new CodeNamespaceImport(nmespace));
        }

        public CodeCastExpression Cast(CodeTypeReference type, CodeExpression exp)
        {
            return new CodeCastExpression(type, exp);
        }

        public void EnsureImports(IEnumerable<string>? nmespaces)
        {
            if (nmespaces == null)
                return;
            foreach (var nmspace in nmespaces)
            {
                EnsureImport(nmspace);
            }
        }

        public CodeTryCatchFinallyStatement Try(
            Action genTryStatements,
            IEnumerable<CodeCatchClause> catchClauses,
            Action genFinallyStatements)
        {
            var t = new CodeTryCatchFinallyStatement();
            var oldScope = Scope;
            Scope = t.TryStatements;
            genTryStatements();
            t.CatchClauses.AddRange(catchClauses);
            Scope = t.FinallyStatements;
            genFinallyStatements();
            Scope = oldScope;
            Scope.Add(t);
            return t;
        }

        public void Break()
        {
            Scope.Add(new CodeBreakStatement());
        }

        public void Continue()
        {
            Scope.Add(new CodeContinueStatement());
        }

        public CodePreTestLoopStatement While(
            CodeExpression exp,
            Action generateBody)
        {
            var w = new CodePreTestLoopStatement
            {
                Test = exp,
            };
            var oldScope = Scope;
            Scope = w.Body;
            generateBody();
            Scope = oldScope;
            Scope.Add(w);
            return w;
        }

        public CodePostTestLoopStatement DoWhile(
            Action generateBody,
            CodeExpression exp)
        {
            var dw = new CodePostTestLoopStatement
            {
                Test = exp,
            };
            Scope.Add(dw);
            var oldScope = Scope;
            Scope = dw.Body;
            generateBody();
            Scope = oldScope;
            return dw;
        }

        public CodeYieldStatement Yield(CodeExpression exp)
        {
            var y = new CodeYieldStatement(exp);
            Scope.Add(y);
            return y;
        }

        public CodeMethodReferenceExpression MethodRef(CodeExpression exp, string methodName)
        {
            return new CodeMethodReferenceExpression(exp, methodName);
        }

        public CodeObjectCreateExpression New(CodeTypeReference type, params CodeExpression[] args)
        {
            var exp = new CodeObjectCreateExpression
            {
                Type = type
            };
            exp.Arguments!.AddRange(args);
            return exp;
        }

        public CodeArrayCreateExpression NewArray(CodeTypeReference type, params CodeExpression[] items)
        {
            var exp = new CodeArrayCreateExpression(type, items);
            return exp;
        }

        public CodeNumericLiteral Number(string sNumber)
        {
            return new CodeNumericLiteral(sNumber);
        }

        public CodeNumericLiteral Number(int number)
        {
            return new CodeNumericLiteral(number.ToString());
        }

        public CodePrimitiveExpression Prim(object? o)
        {
            if (o is long l)
            {
                if (int.MinValue <= l && l < int.MaxValue)
                {
                    o = (int) l;
                }
            }

            return new CodePrimitiveExpression(o);
        }

        public CodeTypeReference TypeRef(Type type)
        {
            return new CodeTypeReference(type);
        }

        public CodeTypeReference TypeRef(string typeName)
        {
            return new CodeTypeReference(typeName);
        }

        public CodeTypeReference TypeRef(string typeName, params string[] genericArgs)
        {
            return new CodeTypeReference(
                typeName,
                genericArgs.Select(ga => new CodeTypeReference(ga)).ToArray());
        }

        public CodeTypeReference TypeRef(string typeName, params CodeTypeReference[] genericArgs)
        {
            return new CodeTypeReference(typeName, genericArgs);
        }

        public CodeAttributeDeclaration CustomAttr(CodeTypeReference typeRef, params CodeAttributeArgument[] args)
        {
            return new CodeAttributeDeclaration
            {
                AttributeType = typeRef,
                Arguments = args.ToList(),
            };
            throw new NotImplementedException();
        }

        public CodeCommentStatement Comment(string comment)
        {
            var c = new CodeCommentStatement(comment);
            Scope.Add(c);
            return c;
        }

        public CodeExpression TypeRefExpr(string typeName)
        {
            return new CodeTypeReferenceExpression(typeName);
        }

        public CodeUsingStatement Using(
            IEnumerable<CodeStatement> initializers,
            Action xlatUsingBody)
        {
            var u = new CodeUsingStatement();
            Scope.Add(u);
            u.Initializers.AddRange(initializers);
            var old = Scope;
            Scope = u.Statements;
            xlatUsingBody();
            Scope = old;
            return u;
        }

        public CodeValueTupleExpression ValueTuple(params CodeExpression[] exprs)
        {
            return new CodeValueTupleExpression(exprs);
        }

        public CodeValueTupleExpression ValueTuple(IEnumerable<CodeExpression> exprs)
        {
            return new CodeValueTupleExpression(exprs.ToArray());
        }

        public CodeMemberProperty PropertyDef(string name, Action generatePropertyGetter, Action generatePropertySetter)
        {
            var prop = new CodeMemberProperty
            {
                Name = name,
                Attributes = MemberAttributes.Public,
                PropertyType = new CodeTypeReference(typeof(object))
            };
            var mem = new CodeMemberProperty();
            var old = Scope;
            var oldMethod = CurrentMember;
            var oldStatements = CurrentStatements;
            var oldComments = CurrentComments;

            SetCurrentPropertyAccessor(prop, prop.GetStatements);
            this.Scope = prop.GetStatements;
            generatePropertyGetter();
            if (generatePropertySetter != null)
            {
                SetCurrentPropertyAccessor(prop, prop.SetStatements);
                this.Scope = prop.SetStatements;
                generatePropertySetter();
            }

            AddMemberWithComments(prop);
            this.Scope = old;
            this.CurrentMember = oldMethod;
            this.CurrentStatements = oldStatements;
            this.CurrentComments = oldComments;

            return prop;
        }

        public CodeLetClause Let(CodeExpression id, CodeExpression value)
        {
            return new CodeLetClause(id, value);
        }

        public CodeWhereClause Where(CodeExpression e)
        {
            return new CodeWhereClause(e);
        }

        public CodeFromClause From(CodeExpression id, CodeExpression collection)
        {
            return new CodeFromClause(id, collection);
        }

        public CodeQueryClause Select(CodeExpression projection)
        {
            return new CodeSelectClause(projection);
        }

        public CodeQueryExpression Query(params CodeQueryClause[] clauses)
        {
            return new CodeQueryExpression(clauses);
        }

        public CodeExpression This()
        {
            return new CodeThisReferenceExpression();
        }

        public CodeMemberField EnumValue(string name, CodeExpression value)
        {
            return new CodeMemberField(typeof(object), name)
            {
                InitExpression = value
            };
        }
    }
}