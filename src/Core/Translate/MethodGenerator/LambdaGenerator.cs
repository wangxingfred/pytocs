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

#nullable enable

using Pytocs.Core.CodeModel;
using Pytocs.Core.Syntax;
using System.Linq;

namespace Pytocs.Core.Translate
{
    public class LambdaGenerator : MethodGenerator
    {
        public LambdaGenerator(
            LambdaStatement lambda,
            TypeReferenceTranslator types,
            CodeGenerator gen)
            : base(null, null, null, lambda, true, false, types, gen)
        {
        }

        protected override ICodeFunction Generate(CodeTypeReference retType,
            CodeParameterDeclarationExpression[] parameters)
        {
            var declaration = GenerateLambdaVariable();

            var method = gen.LambdaMethod(declaration, parameters, () => Xlat(MethodBody));
            // GenerateTupleParameterUnpackers(method);
            // LocalVariableGenerator.Generate(method, globals);
            return method;
        }

        private CodeVariableDeclarationStatement GenerateLambdaVariable()
        {
            var type = this.gen.TypeRef("Func", Enumerable.Range(0, MethodParams.Count + 1)
                .Select(x => "object")
                .ToArray());
            return new CodeVariableDeclarationStatement(type, MethodName);
        }
    }
}
