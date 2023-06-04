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

using Pytocs.Core.CodeModel;
using Pytocs.Core.Syntax;
using Pytocs.Core.TypeInference;
using Pytocs.Core.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pytocs.Core.Translate
{
    public class ConstructorGenerator : MethodGenerator
    {
        public ConstructorGenerator(
            string className,
            ClassDef? classDef,
            ClassType? classType,
            FunctionDef f,
            List<Parameter> parameters,
            TypeReferenceTranslator types,
            CodeGenerator gen)
            : base(className, classDef, classType, f, false, false, types, gen)
        {
            MethodParams = parameters;
        }

        protected override string MethodName => classDef!.name.Name;

        protected override List<Parameter> MethodParams { get; }

        protected override CodeMemberMethod Generate(CodeTypeReference ignore, CodeParameterDeclarationExpression[] parms)
        {
            var cons = gen.Constructor(parms, () => XlatConstructor(MethodBody));
            GenerateTupleParameterUnpackers(cons);
            LocalVariableGenerator.Generate(cons, globals);
            return cons;
        }

        private void XlatConstructor(SuiteStatement stmt)
        {
            if (stmt == null)
                return;

            var comments = StatementTranslator.ConvertFirstStringToComments(stmt.Statements);
            stmt.Accept(this.stmtXlat);
            if (gen.Scope.Count == 0)
                return;
            gen.Scope[0].ToString();
            if (!(gen.Scope[0] is CodeExpressionStatement expStm))
                return;
            if (!(expStm.Expression is CodeApplicationExpression appl))
                return;
            if (!(appl.Method is CodeFieldReferenceExpression method) || method.FieldName != "_Ctor")
                return;
            var ctor = (CodeConstructor) gen.CurrentMember!;
            ctor.Comments.AddRange(comments);
            ctor.BaseConstructorArgs.AddRange(appl.Arguments.Skip(1));
            gen.Scope.RemoveAt(0);
        }
        }
}
