using System.Collections.Generic;
namespace Pytocs.Core.CodeModel;

public class CodeForStatement : CodeStatement
{
    public CodeForStatement(
        CodeStatement initializer, CodeExpression condition, CodeStatement increment)
    {
        // Variable = variable;
        // Collection = collection;
        Initializer = initializer;
        Condition = condition;
        Increment = increment;
        Statements = new List<CodeStatement>();
    }

    // for (var i = 0; i < collection.Count; i = i + n)
    // {
    //     var value = collection[i];
    // }

    // public CodeVariableReferenceExpression Variable { get; set; }        // i
    // public CodeExpression Collection { get; set; }      // collection

    public CodeStatement Initializer { get; set; }     // var i = 0
    public CodeExpression Condition { get; set; }       // i < collection.Count
    public CodeStatement Increment { get; set; }       // i += n
    public List<CodeStatement> Statements { get; private set; }

    public override T Accept<T>(ICodeStatementVisitor<T> visitor)
    {
        return visitor.VisitFor(this);
    }
}