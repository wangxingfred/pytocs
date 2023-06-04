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
using Pytocs.Core.Translate;
using Pytocs.Core.Types;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Pytocs.UnitTests.Translate
{
    public class LocalVariableTranslator
    {
        public LocalVariableTranslator()
        {
        }

        private string XlatModule(string pyModule)
        {
            var rdr = new StringReader(pyModule);
            var lex = new Lexer("foo.py", rdr);
            var par = new Parser("foo.py", lex);
            var stm = par.stmt();
            var unt = new CodeCompileUnit();
            var gen = new CodeGenerator(unt, "test", "testModule");
            var sym = new SymbolGenerator();
            var types = new TypeReferenceTranslator(new Dictionary<Node, DataType>());
            var xlt = new StatementTranslator(types, gen, sym, new HashSet<string>());
            stm[0].Accept(xlt);
            var pvd = new CSharpCodeProvider();
            var writer = new StringWriter();
            foreach (CodeNamespace ns in unt.Namespaces)
            {
                foreach (CodeNamespaceImport imp in ns.Imports)
                {
                    writer.WriteLine("using {0};", SanitizeNamespace(imp.Namespace, gen));
                }

                foreach (CodeTypeDeclaration type in ns.Types)
                {
                    pvd.GenerateCodeFromType(
                        type,
                        writer,
                        new CodeGeneratorOptions
                        {
                        });
                }
            }

            return writer.ToString();
        }

        private string XlatMember(string pyModule)
        {
            var rdr = new StringReader(pyModule);
            var lex = new Lexer("foo.py", rdr);
            var par = new Parser("foo.py", lex);
            var stm = par.stmt();
            var unt = new CodeCompileUnit();
            var gen = new CodeGenerator(unt, "test", "testModule");
            var sym = new SymbolGenerator();
            var types = new TypeReferenceTranslator(new Dictionary<Node, DataType>());
            var xlt = new StatementTranslator(types, gen, sym, new HashSet<string>());
            var stm0 = stm[0];
            stm0.Accept(xlt);
            var pvd = new CSharpCodeProvider();
            var writer = new StringWriter();
            foreach (CodeNamespace ns in unt.Namespaces)
            {
                foreach (var member in ns.Types[0].Members)
                {
                    pvd.GenerateCodeFromMember(
                        member, writer,
                        new CodeGeneratorOptions
                        {
                        });
                    writer.WriteLine();
                }
            }

            return writer.ToString();
        }

        /// <summary>
        /// Ensures no component of the namespace is a C# keyword.
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        private string SanitizeNamespace(string nmspace, CodeGenerator gen)
        {
            return string.Join(".",
                nmspace.Split('.')
                    .Select(n => gen.EscapeKeywordName(n)));
        }

        [Fact]
        public void Lvt_IfElseDeclaration()
        {
            const string luaSrc =
@"function foo()
    local y
    if self.x then
        y = 3
    else
        y = 9
    end
    self.y = y * 2
end";

            const string sExp =
@"public static object foo() {
    object y;
    if (this.x) {
        y = 3;
    } else {
        y = 9;
    }
    this.y = y * 2;
}

";
            Assert.Equal(sExp, XlatMember(luaSrc));
        }

        [Fact]
        public void Lvi_ForceStandAloneDefinition()
        {
            var luaSrc =
@"function foo()
    local x
    if self.x then
        x = self.x
    end
    x = x + 1
end";

            var sExp =
@"public static object foo() {
    object x;
    if (this.x) {
        x = this.x;
    }
    x = x + 1;
}

";
            Assert.Equal(sExp, XlatMember(luaSrc));
        }

        [Fact]
        public void Lvi_LocalRedefinition()
        {
            var luaSrc =
@"function foo()
    if self.x then
        local x = self.x
        x = x + 1
        self.x = x
    end
end";

            var sExp =
@"public static object foo() {
    if (this.x) {
        var x = this.x;
        x = x + 1;
        this.x = x;
    }
}

";
            Assert.Equal(sExp, XlatMember(luaSrc));
        }

        [Fact]
        public void Lvi_LocalInBranch()
        {
            var luaSrc =
@"function foo()
    if self.x then
        local x = self.x
        self.x = nil
        x.foo()
    end
end";

            var sExp =
@"public static object foo() {
    if (this.x) {
        var x = this.x;
        this.x = null;
        x.foo();
    }
}

";
            Assert.Equal(sExp, XlatMember(luaSrc));
        }

        [Fact]
        public void Lvi_ModifyParameter()
        {
            var luaSrc =
@"function foo(frog)
    if frog == nil then
        frog = 'default'
    end
    bar(frog)
end";

            var sExp =
@"public static object foo(object frog) {
    if (frog == null) {
        frog = ""default"";
    }
    bar(frog);
}

";
            Assert.Equal(sExp, XlatMember(luaSrc));
        }

        [Fact]
        public void Liv_ChainedIfElses()
        {
            var luaSrc =
@"function fn(arg)
    local result
    if arg == 1 then
        result = 'one'
    elseif arg == 2 then
        result = 'two'
    elseif arg == 3 then
        result = 'three'
    else
        result = 'many'
    end
    return result
end";
            var sExp =
@"public static object fn(object arg) {
    object result;
    if (arg == 1) {
        result = ""one"";
    } else if (arg == 2) {
        result = ""two"";
    } else if (arg == 3) {
        result = ""three"";
    } else {
        result = ""many"";
    }
    return result;
}

";
            Assert.Equal(sExp, XlatMember(luaSrc));
        }

        [Fact(DisplayName = nameof(Lv_AssignmentExpression))]
        public void Lv_AssignmentExpression()
        {
            var pySrc =
@"function foo()
    local chunk = read(256)
    while chunk ~= nil do
        process(chunk)
        chunk = read(256)
    end
end";
            var sExp =
@"public static object foo() {
    var chunk = read(256);
    while (chunk != null) {
        process(chunk);
        chunk = read(256);
    }
}

";
            Assert.Equal(sExp, XlatMember(pySrc));
        }

        [Fact(DisplayName = nameof(Lv_Assign_Assign))]
        public void Lv_Assign_Assign()
        {
            var pySrc =
@"function func()
    local x, y = other_func(3), z
    yet_another_func(x, y, z)
end";
            var sExpected =
                @"public static object func() {
    var x = other_func(3);
    var y = z;
    yet_another_func(x, y, z);
}

";
            Assert.Equal(sExpected, XlatMember(pySrc));
        }
    }
}