using System;

namespace Pytocs.Core.Types;

public class EmptyTableType : DataType
{
    public override T Accept<T>(IDataTypeVisitor<T> visitor)
    {
        return visitor.VisitEmptyTable(this);
    }

    public override DataType MakeGenericType(params DataType[] typeArguments)
    {
        throw new NotImplementedException();
    }
}