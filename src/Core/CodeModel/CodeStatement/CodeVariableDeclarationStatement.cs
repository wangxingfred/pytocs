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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pytocs.Core.CodeModel
{
    public class CodeVariableDeclarationStatement : CodeStatement
    {
        public static CodeVariableDeclarationStatement CreateVar(string name, CodeExpression init)
        {
            return new CodeVariableDeclarationStatement("var", name)
            {
                InitExpression = init
            };
        }

        public static CodeVariableDeclarationStatement CreateVar(CodeTypeReference type, string name)
        {
            if (type.TypeName == "object")
            {
                return CreateDynamic(name);
            }

            return new CodeVariableDeclarationStatement(type.TypeName, name);
        }

        public static CodeVariableDeclarationStatement CreateDynamic(string name)
        {
            return new CodeVariableDeclarationStatement("dynamic", name);
        }

        public static CodeVariableDeclarationStatement CreateObject(string name, CodeExpression? init)
        {
            return new CodeVariableDeclarationStatement("object", name)
            {
                InitExpression = init
            };
        }


        public CodeVariableDeclarationStatement(string typeName, string name)
        {
            this.Type = new CodeTypeReference(typeName);
            this.Name = name;
        }

        public CodeVariableDeclarationStatement(CodeTypeReference type, string name)
        {
            this.Type = type;
            this.Name = name;
        }

        public CodeTypeReference Type { get; set; }
        public string Name { get; set; }
        public CodeExpression? InitExpression { get; set; }

        public override T Accept<T>(ICodeStatementVisitor<T> visitor)
        {
            return visitor.VisitVariableDeclaration(this);
        }
    }
}
