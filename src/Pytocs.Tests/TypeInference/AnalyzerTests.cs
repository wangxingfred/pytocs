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

using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pytocs.Core.TypeInference;
using Pytocs.Core;
using Xunit.Abstractions;

namespace Pytocs.UnitTests.TypeInference
{
    public class AnalyzerTests
    {
        private readonly ITestOutputHelper testOutputHelper;
        private readonly FakeFileSystem fs;
        private readonly ILogger logger;
        private readonly Dictionary<string, object> options;
        private readonly string nl;
        // private readonly AnalyzerImpl an;

        public AnalyzerTests(ITestOutputHelper testOutputHelper)
        {
            this.testOutputHelper = testOutputHelper;
            this.fs = new FakeFileSystem();
            this.logger = new FakeLogger();
            this.options = new Dictionary<string, object>();
            this.nl = Environment.NewLine;
            // this.an = new AnalyzerImpl(fs, logger, options, DateTime.Now);
        }

        private AnalyzerImpl NewAnalyzer()
        {
            return new AnalyzerImpl(fs, logger, options, DateTime.Now);
        }
        
        private void ExpectBindings(AnalyzerImpl an, string sExp)
        {
            var sActual = BindingsToString(an);
            testOutputHelper.WriteLine(sActual);
            if (sExp != sActual)
            {
                var split = new string[] { nl };
                var opt = StringSplitOptions.None;
                var aExp = sExp.Split(split, opt);
                var aActual = sActual.Split(split, opt);
                int i;
                for (i = 0; i < Math.Min(aExp.Length, aActual.Length); ++i)
                {
                    Assert.Equal($"{i}:{aExp[i]}", $"{i}:{aActual[i]}");
                }
                Assert.False(i < aExp.Length, $"Fewer than the expected {aExp.Length} lines.");
                Assert.True(i > aExp.Length, $"More than the expected {aExp.Length} lines.");
                Assert.Equal(sExp, BindingsToString(an));
            }
        }
        
        private static string BindingsToString(AnalyzerImpl an)
        {
            var sb = new StringBuilder();
            var e = an.GetAllBindings().Where(b => !b.IsBuiltin && !b.IsSynthetic).GetEnumerator();
            while (e.MoveNext())
            {
                sb.AppendLine(e.Current.ToString());
            }
            return sb.ToString();
        }

        [Fact]
        public void TypeAn_Empty()
        {
            var an = NewAnalyzer();
            an.Analyze("\\foo");
        }

        [Fact]
        public void TypeAn_StrDef_Local()
        {
            var an = NewAnalyzer();
            
            fs.Dir("foo")
                .File("test.py", "local x = 'hello world'\n");
            an.Analyze("\\foo");
            var sExp =
                @"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
                @"(binding:kind=SCOPE:node=x:type=str:qname=.foo.test.x:refs=[])" + nl;

            var sActual = BindingsToString(an);
            testOutputHelper.WriteLine(sActual);
            Assert.Equal(sExp, sActual);
        }
        
        [Fact]
        public void TypeAn_StrDef_Global()
        {
            var an = NewAnalyzer();
            
            fs.Dir("foo")
                .File("test.py", "x = 'hello world'\n");
            an.Analyze("\\foo");
            var sExp =
                @"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
                @"(binding:kind=SCOPE:node=x:type=str:qname=x:refs=[])" + nl;

            var sActual = BindingsToString(an);
            testOutputHelper.WriteLine(sActual);
            Assert.Equal(sExp, sActual);
        }

        [Fact]
        public void TypeAn_Copy()
        {
            var an = NewAnalyzer();
            fs.Dir("foo")
                .File("test.py", "local x = 3\nlocal y = x\n");
            an.Analyze(@"\foo");
            var sExp =
                @"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
                @"(binding:kind=SCOPE:node=x:type=int:qname=.foo.test.x:refs=[x])" + nl +
                @"(binding:kind=SCOPE:node=y:type=int:qname=.foo.test.y:refs=[])" + nl;

            var sActual = BindingsToString(an);
            testOutputHelper.WriteLine(sActual);
            Assert.Equal(sExp, sActual);
        }

        [Fact]
        public void TypeAn_FuncDef_Local()
        {
            var an = NewAnalyzer();
            
            fs.Dir("foo")
                .File("test.py",
@"
local x = 'default'
local function crunk(a)
    local x
    if x ~= 'default' then
        print('Yo')
    end
    print(a)
    x = ''
    return 'fun'
end
");
            an.Analyze(@"\foo");
            var sExp =
@"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
@"(binding:kind=SCOPE:node=x:type=str:qname=.foo.test.x:refs=[])" + nl +
@"(binding:kind=FUNCTION:node=crunk:type=? -> str:qname=.foo.test.crunk:refs=[])" + nl +
// @"(binding:kind=SCOPE:node=x:type=str:qname=.foo.test.crunk.x:refs=[x])" + nl +
@"(binding:kind=PARAMETER:node=a:type=?:qname=.foo.test.crunk.a:refs=[a])" + nl +
@"(binding:kind=VARIABLE:node=x:type=str:qname=.foo.test.crunk.x:refs=[x,x])" + nl;

            var sActual = BindingsToString(an);
            testOutputHelper.WriteLine(sActual);
            Assert.Equal(sExp, sActual);
        }
        
        [Fact]
        public void TypeAn_FuncDef_Local2()
        {
            var an = NewAnalyzer();
            fs.Dir("foo")
                .File("test.py",
                    @"
local x, y = 'default'
local function crunk(a)
    local x, z
    if x ~= 'default' then
        print('Yo')
    end
    print(a)
    x = ''
    return 'fun'
end
");
            an.Analyze(@"\foo");
            var sExp =
                @"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
                @"(binding:kind=SCOPE:node=x:type=str:qname=.foo.test.x:refs=[])" + nl +
                @"(binding:kind=SCOPE:node=y:type=?:qname=.foo.test.y:refs=[])" + nl +
                @"(binding:kind=FUNCTION:node=crunk:type=? -> str:qname=.foo.test.crunk:refs=[])" + nl +
                @"(binding:kind=PARAMETER:node=a:type=?:qname=.foo.test.crunk.a:refs=[a])" + nl +
                @"(binding:kind=VARIABLE:node=x:type=str:qname=.foo.test.crunk.x:refs=[x,x])" + nl +
                @"(binding:kind=VARIABLE:node=z:type=?:qname=.foo.test.crunk.z:refs=[])" + nl;

            var sActual = BindingsToString(an);
            testOutputHelper.WriteLine(sActual);
            Assert.Equal(sExp, sActual);
        }

        [Fact]
        public void TypeAn_FuncDef_Globals()
        {
            var an = NewAnalyzer();
            fs.Dir("foo")
                .File("test.py",
@"
x = 'default'
function crunk(a)
    if x ~= 'default' then
        print('Yo')
    end
    print(a)
    x = ''
    return 'fun'
end
");
            an.Analyze(@"\foo");
            an.Finish();
            var sExp =
@"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
@"(binding:kind=SCOPE:node=x:type=str:qname=x:refs=[x,x])" + nl +
@"(binding:kind=FUNCTION:node=crunk:type=? -> str:qname=crunk:refs=[])" + nl +
@"(binding:kind=PARAMETER:node=a:type=?:qname=crunk.a:refs=[a])" + nl;

            var sActual = BindingsToString(an);
            testOutputHelper.WriteLine(sActual);
            Assert.Equal(sExp, sActual);
        }

        [Fact(DisplayName = nameof(TypeAn_FwdReference))]
        public void TypeAn_FwdReference()
        {
            var an = NewAnalyzer();
            fs.Dir("foo")
                .File("test.py",
@"
local function foo(x)
    print('foo ' .. x)
    bar(x)
end


function bar(y)
    print('bar ' .. y)
end

foo('Hello')
");
            an.Analyze(@"\foo");
            an.Finish();
            var sExp =
@"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
@"(binding:kind=FUNCTION:node=foo:type=str -> Unit:qname=.foo.test.foo:refs=[foo])" + nl +
@"(binding:kind=PARAMETER:node=x:type=str:qname=.foo.test.foo.x:refs=[x,x])" + nl +
@"(binding:kind=FUNCTION:node=bar:type=str -> Unit:qname=bar:refs=[bar])" + nl +
@"(binding:kind=PARAMETER:node=y:type=str:qname=bar.y:refs=[y])" + nl;

            var sActual = BindingsToString(an);
            testOutputHelper.WriteLine(sActual);
            Assert.Equal(sExp, sActual);
        }

        [Fact(DisplayName = "TypeAn_LocalVar")]
        public void TypeAn_LocalVar()
        {
            var an = NewAnalyzer();
            fs.Dir("foo")
                .File("test.py",
@"
x = 'string'    -- global var
local function bar()
    local x = 3 -- local var
    print(x)
end");
            an.Analyze(@"\foo");
            an.Finish();
            var sExp =
@"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
@"(binding:kind=SCOPE:node=x:type=str:qname=x:refs=[])" + nl +
@"(binding:kind=FUNCTION:node=bar:type=() -> Unit:qname=.foo.test.bar:refs=[])" + nl +
@"(binding:kind=VARIABLE:node=x:type=int:qname=.foo.test.bar.x:refs=[x])" + nl;
            
            var sActual = BindingsToString(an);
            testOutputHelper.WriteLine(sActual);
            Assert.Equal(sExp, sActual);
        }

        [Fact]
        public void TypeAn_Point()
        {
            var an = NewAnalyzer();
            fs.Dir("foo")
                .File("test.py",
@"
local function bar(point)
    return sqrt(point.x * point.x + point.y * point.y)
end");
            an.Analyze(@"\foo");
            an.Finish();
            var sExp =
@"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
@"(binding:kind=FUNCTION:node=bar:type=? -> ?:qname=.foo.test.bar:refs=[])" + nl +
@"(binding:kind=PARAMETER:node=point:type=?:qname=.foo.test.bar.point:refs=[point,point,point,point])" + nl;

            var sActual = BindingsToString(an);
            testOutputHelper.WriteLine(sActual);
            Assert.Equal(sExp, sActual);
        }
        

        [Fact(DisplayName = "TypeAn_Array_Ref")]
        public void TypeAn_Array_Ref()
        {
            var an = NewAnalyzer();
            
            fs.Dir("foo")

                .File("test.py",
@"
local function bar()
    local s = {'bar'}
    s[0] = 'foo'
end");
            an.Analyze(@"\foo");
            an.Finish();
            var sExp =
@"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
@"(binding:kind=FUNCTION:node=bar:type=() -> Unit:qname=.foo.test.bar:refs=[])" + nl +
@"(binding:kind=VARIABLE:node=s:type=[str]:qname=.foo.test.bar.s:refs=[s])" + nl;

            ExpectBindings(an, sExp);
        }

        [Fact]
        public void TypeAn_Bool_Local()
        {
            var an = NewAnalyzer();
            
            fs.Dir("foo")
                .File("test.py",
@"local function fn()
    local ret = true
    return ret
end");
            an.Analyze(@"\foo");
            an.Finish();
            var sExp =
@"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
@"(binding:kind=FUNCTION:node=fn:type=() -> bool:qname=.foo.test.fn:refs=[])" + nl +
@"(binding:kind=VARIABLE:node=ret:type=bool:qname=.foo.test.fn.ret:refs=[ret])" + nl;

            ExpectBindings(an, sExp);
        }
        
        [Fact]
        public void TypeAn_module_GlobalStyle()
        {
            var an = NewAnalyzer();

            fs.Dir("foo")
                .File("test.py",
@"
Boot = {}

Boot.aa = true
Boot.bb = nil

function Boot.Start()
end
");
            
            an.Analyze(@"\foo");
            
        
            an.Finish();
            var sExp =
                @"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
                @"(binding:kind=CLASS:node=Boot:type=<Boot>:qname=Boot:refs=[Boot,Boot])" + nl +
                @"(binding:kind=ATTRIBUTE:node=Boot.aa:type=bool:qname=Boot.aa:refs=[])" + nl +
                @"(binding:kind=ATTRIBUTE:node=Boot.bb:type=None:qname=Boot.bb:refs=[])" + nl +
                @"(binding:kind=METHOD:node=Start:type=() -> Unit:qname=Boot.Start:refs=[])";

            ExpectBindings(an, sExp);
        }

        [Fact]
        public void TypeAn_module_LocalStyle()
        {
            
        }
        

        [Fact(DisplayName = nameof(TypeAn_class_instance_creation))]
        public void TypeAn_class_instance_creation()
        {
            var an = NewAnalyzer();
            
            fs.Dir("foo")
                .File("cls.py",
@"
class Cls:
    def echo(self, s):
        print(s)
")
                .File("test.py",
@"
def bar():
    c = cls.Cls()
    c.echo(""Hello"")
");
            an.Analyze(@"\foo");
            an.Finish();
            var sExp =
@"(binding:kind=MODULE:node=(module:\foo\cls.py):type=cls:qname=.foo.cls:refs=[])" + nl +
@"(binding:kind=CLASS:node=Cls:type=<Cls>:qname=.foo.cls.Cls:refs=[])" + nl +
@"(binding:kind=METHOD:node=echo:type=(Cls, ?) -> Unit:qname=.foo.cls.Cls.echo:refs=[])" + nl +
@"(binding:kind=PARAMETER:node=self:type=<Cls>:qname=.foo.cls.Cls.echo.self:refs=[])" + nl +
@"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
@"(binding:kind=FUNCTION:node=bar:type=() -> Unit:qname=.foo.test.bar:refs=[])" + nl +
@"(binding:kind=SCOPE:node=c:type=?:qname=.foo.test.bar.c:refs=[c])" + nl +
@"(binding:kind=PARAMETER:node=self:type=Cls:qname=.foo.cls.Cls.echo.self:refs=[])" + nl +
@"(binding:kind=PARAMETER:node=s:type=?:qname=.foo.cls.Cls.echo.s:refs=[s])" + nl +
@"(binding:kind=VARIABLE:node=c:type=?:qname=.foo.test.bar.c:refs=[c])" + nl;

            ExpectBindings(an, sExp);
        }
        
        [Fact(DisplayName = nameof(TypeAn_Attribute))]
        public void TypeAn_Attribute()
        {
            var an = NewAnalyzer();
            fs.Dir("foo")
                .File("test.py",
                    @"
class Foo(Object):
    def foo(self):
        print 'foo ' + self.x,

    self.bar(x)

    def bar(self, y):
        print 'bar ' + y

f = Foo('Hello')
f.foo()
");
            an.Analyze(@"\foo");
            an.Finish();
            var sExp =
                @"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
                @"(binding:kind=CLASS:node=Foo:type=<Foo>:qname=.foo.test.Foo:refs=[Foo])" + nl +
                @"(binding:kind=METHOD:node=foo:type=Foo -> Unit:qname=.foo.test.Foo.foo:refs=[f.foo])" + nl +
                @"(binding:kind=PARAMETER:node=self:type=<Foo>:qname=.foo.test.Foo.foo.self:refs=[self])" + nl +
                @"(binding:kind=METHOD:node=bar:type=(Foo, ?) -> Unit:qname=.foo.test.Foo.bar:refs=[])" + nl +
                @"(binding:kind=PARAMETER:node=self:type=<Foo>:qname=.foo.test.Foo.bar.self:refs=[])" + nl +
                @"(binding:kind=SCOPE:node=f:type=Foo:qname=.foo.test.f:refs=[f])" + nl +
                @"(binding:kind=PARAMETER:node=self:type=Foo:qname=.foo.test.Foo.foo.self:refs=[self])" + nl +
                @"(binding:kind=PARAMETER:node=self:type=Foo:qname=.foo.test.Foo.bar.self:refs=[])" + nl +
                @"(binding:kind=PARAMETER:node=y:type=?:qname=.foo.test.Foo.bar.y:refs=[y])" + nl;

            var sActual = BindingsToString(an);
            testOutputHelper.WriteLine(sActual);
            Assert.Equal(sExp, sActual);
        }
        
        [Fact(DisplayName = nameof(TypeAn_Dirs))]
        public void TypeAn_Dirs()
        {
            var an = NewAnalyzer();
            
            fs.Dir("sys_q")
                .Dir("parsing")
                    .File("__init__.py", "")
                    .File("parser.py",
@"
class Parser(object):
    def parse(self, phile):
        pass
")
                .End()
                .File("main.py",
@"
from parsing.parser import Parser

def mane_lupe(phile):
    p = Parser()
    p.parse(phile)
");
            an.Analyze(@"\sys_q");
            an.Finish();
            var sExp =
@"(binding:kind=MODULE:node=(module:\sys_q\parsing\__init__.py):type=:qname=:refs=[])" + nl +
@"(binding:kind=MODULE:node=(module:\sys_q\parsing\parser.py):type=parser:qname=.sys_q.parsing.parser:refs=[])" + nl +
@"(binding:kind=CLASS:node=Parser:type=<Parser>:qname=.sys_q.parsing.parser.Parser:refs=[Parser,Parser])" + nl +
@"(binding:kind=METHOD:node=parse:type=(Parser, ?) -> Unit:qname=.sys_q.parsing.parser.Parser.parse:refs=[p.parse])" + nl +
@"(binding:kind=PARAMETER:node=self:type=<Parser>:qname=.sys_q.parsing.parser.Parser.parse.self:refs=[])" + nl +

@"(binding:kind=MODULE:node=(module:\sys_q\main.py):type=main:qname=.sys_q.main:refs=[])" + nl +
@"(binding:kind=VARIABLE:node=parsing:type=:qname=:refs=[])" + nl +
@"(binding:kind=VARIABLE:node=parser:type=parser:qname=.sys_q.parsing.parser:refs=[])" + nl +
@"(binding:kind=FUNCTION:node=mane_lupe:type=? -> Unit:qname=.sys_q.main.mane_lupe:refs=[])" + nl +
@"(binding:kind=SCOPE:node=p:type=Parser:qname=.sys_q.main.mane_lupe.p:refs=[p])" + nl +
@"(binding:kind=PARAMETER:node=self:type=Parser:qname=.sys_q.parsing.parser.Parser.parse.self:refs=[])" + nl +
@"(binding:kind=PARAMETER:node=phile:type=?:qname=.sys_q.parsing.parser.Parser.parse.phile:refs=[])" + nl +
@"(binding:kind=PARAMETER:node=phile:type=?:qname=.sys_q.main.mane_lupe.phile:refs=[phile])" + nl +
@"(binding:kind=VARIABLE:node=p:type=Parser:qname=.sys_q.main.mane_lupe.p:refs=[p])" + nl;
            ExpectBindings(an, sExp);
        }
        
        [Fact(DisplayName = nameof(TypeAn_Inherit_field))]
        public void TypeAn_Inherit_field()
        {
            var an = NewAnalyzer();
            fs.Dir("foo")
                .File("test.py",
@"class Base():
    def __init__():
        this.field = ""hello""

class Derived(Base):
    def foo():
        print(this.field)
");
            an.Analyze(@"\foo");
            an.Finish();
            var sExp =
@"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
@"(binding:kind=CLASS:node=Base:type=<Base>:qname=.foo.test.Base:refs=[Base])" + nl +
@"(binding:kind=CONSTRUCTOR:node=__init__:type=() -> Unit:qname=.foo.test.Base.__init__:refs=[])" + nl +
@"(binding:kind=CLASS:node=Derived:type=<Derived>:qname=.foo.test.Derived:refs=[])" + nl +
@"(binding:kind=METHOD:node=foo:type=() -> Unit:qname=.foo.test.Derived.foo:refs=[])" + nl;

            ExpectBindings(an, sExp);
        }

        // Reported in https://github.com/uxmal/pytocs/issues/51
//         [Fact(DisplayName = nameof(TypeAn_async_function))]
//         public void TypeAn_async_function()
//         {
//             var an = NewAnalyzer();
//             fs.Dir("foo")
//                 .File("test.py",
// @"async def foo(field) -> bool:
//     return field == ""hello""
// ");
//             an.Analyze(@"\foo");
//             an.Finish();
//             var sExp =
// @"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
// @"(binding:kind=FUNCTION:node=foo:type=? -> bool:qname=.foo.test.foo:refs=[])" + nl +
// @"(binding:kind=PARAMETER:node=field:type=?:qname=foo.field:refs=[field])" + nl;
//             ExpectBindings(an, sExp);
//         }

        [Fact(DisplayName = nameof(TypeAn_call_Ctor))]
        public void TypeAn_call_Ctor()
        {
            var an = NewAnalyzer();
            fs.Dir("foo")
                .File("test.py",
@"class Foo():
    def __init__(self, name):
        this.name = name

    def bar(self):
        return Foo(""bar"")
");
            an.Analyze(@"\foo");
            an.Finish();
            var sExp =
            #region Expected 
@"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])
(binding:kind=CLASS:node=Foo:type=<Foo>:qname=.foo.test.Foo:refs=[Foo])
(binding:kind=CONSTRUCTOR:node=__init__:type=(Foo, str) -> Unit:qname=.foo.test.Foo.__init__:refs=[])
(binding:kind=PARAMETER:node=self:type=<Foo>:qname=.foo.test.Foo.__init__.self:refs=[])
(binding:kind=METHOD:node=bar:type=Foo -> Foo:qname=.foo.test.Foo.bar:refs=[])
(binding:kind=PARAMETER:node=self:type=<Foo>:qname=.foo.test.Foo.bar.self:refs=[])
(binding:kind=PARAMETER:node=self:type=Foo:qname=.foo.test.Foo.__init__.self:refs=[])
(binding:kind=PARAMETER:node=name:type=str:qname=.foo.test.Foo.__init__.name:refs=[name])
(binding:kind=PARAMETER:node=self:type=Foo:qname=.foo.test.Foo.bar.self:refs=[])
";
            #endregion
            ExpectBindings(an, sExp);
        }

        [Fact]
        public void TypeAn_call_Ctor_names()
        {
            var an = NewAnalyzer();
            fs.Dir("foo")
                .File("test.py",
@"class Foo():
    def __init__(self, name):
        this.name = name

    def make(name):
        return Foo(name)

x = make('foo')
y = make('bar')
");
            an.Analyze("foo");
            an.Finish();

        }

        [Fact(DisplayName = nameof(TypeAn_void_function))]
        public void TypeAn_void_function()
        {
            var an = NewAnalyzer();
            fs.Dir("foo")
                .File("test.py",
@"def foo(s):
    print(s)
");

            an.Analyze(@"\foo");
            an.Finish();
            var sExp =
@"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
@"(binding:kind=FUNCTION:node=foo:type=? -> Unit:qname=.foo.test.foo:refs=[])" + nl +
@"(binding:kind=PARAMETER:node=s:type=?:qname=.foo.test.foo.s:refs=[s])" + nl;
            ExpectBindings(an, sExp);
        }

//         [Fact(DisplayName = nameof(TypeAn_typed_parameters))]
//         public void TypeAn_typed_parameters()
//         {
//             var an = NewAnalyzer();
//             fs.Dir("foo")
//                 .File("test.py",
// @"def foo(s: str) -> int:
//     return int(s)
// ");
//             an.Analyze(@"\foo");
//             an.Finish();
//             var sExp =
// @"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
// @"(binding:kind=FUNCTION:node=foo:type=str -> int:qname=.foo.test.foo:refs=[])" + nl +
// @"(binding:kind=PARAMETER:node=s:type=str:qname=.foo.test.foo.s:refs=[s])" + nl;
//             ExpectBindings(an, sExp);
//         }

//         [Fact(DisplayName = nameof(TypeAn_typed_list_parameter))]
//         public void TypeAn_typed_list_parameter()
//         {
//             var an = NewAnalyzer();
//             fs.Dir("foo")
//                 .File("test.py",
// @"def foo(s: List[str]) -> int:
//     return bar(s)
// ");
//             an.Analyze(@"\foo");
//             an.Finish();
//             var sExp =
// @"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
// @"(binding:kind=FUNCTION:node=foo:type=[str] -> int:qname=.foo.test.foo:refs=[])" + nl +
// @"(binding:kind=PARAMETER:node=s:type=[str]:qname=.foo.test.foo.s:refs=[s])" + nl;
//             ExpectBindings(an, sExp);
//         }

//         [Fact(DisplayName = nameof(TypeAn_list_of_tuples))]
//         public void TypeAn_list_of_tuples()
//         {
//             var an = NewAnalyzer();
//             
//             fs.Dir("foo")
//         .File("test.py",
// @"records = [
//     ('a', 3, 8),
//     ('b', 14, -3)
// ]");
//             an.Analyze(@"\foo");
//             an.Finish();
//             var sExp =
// @"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
// @"(binding:kind=SCOPE:node=records:type=[(str, int, int)]:qname=.foo.test.records:refs=[])" + nl;
//             ExpectBindings(an, sExp);
//         }

//         [Fact(DisplayName = nameof(TypeAn_list_of_unequal_sized_tuples))]
//         public void TypeAn_list_of_unequal_sized_tuples()
//         {
//             var an = NewAnalyzer();
//             fs.Dir("foo")
//                 .File("test.py",
// @"records = [
//     ('malloc', 'size_t'),
//     ('memset', 'void *', 'char', 'size_t')
// ]");
//             an.Analyze(@"\foo");
//             an.Finish();
//             var sExp =
// @"(binding:kind=MODULE:node=(module:\foo\test.py):type=test:qname=.foo.test:refs=[])" + nl +
// @"(binding:kind=SCOPE:node=records:type=[(str, str, ...)]:qname=.foo.test.records:refs=[])" + nl;
//             ExpectBindings(an, sExp);
//         }
    }
}
