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

using System.Collections.Generic;
using NameScope = Pytocs.Core.TypeInference.NameScope;
using NameScopeType = Pytocs.Core.TypeInference.NameScopeType;

namespace Pytocs.Core.Types
{
    public class InstanceType : DataType
    {
        public ClassType classType;

        public InstanceType(ClassType c) : base(NameScopeType.INSTANCE)
        {
            Scope.AddSuperClass(c.Scope);
            Scope.Path = c.Scope.Path;
            classType = c;
        }

        public override T Accept<T>(IDataTypeVisitor<T> visitor)
        {
            return visitor.VisitInstance(this);
        }

        public override bool Equals(object? other)
        {
            if (other is InstanceType that)
            {
                return this.classType.Equals(that.classType);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return classType.GetHashCode();
        }

        public override DataType MakeGenericType(params DataType[] typeArguments)
        {
            return new InstanceType(classType.MakeGenericType(typeArguments));
    }
    }
}