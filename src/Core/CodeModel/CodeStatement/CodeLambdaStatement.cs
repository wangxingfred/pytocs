namespace Pytocs.Core.CodeModel;

public class CodeLambdaStatement : CodeLocalFunction
{
    public CodeLambdaStatement(CodeVariableDeclarationStatement declaration)
    {
        Declaration = declaration;
    }

    public CodeVariableDeclarationStatement Declaration { get; }


    public override T Accept<T>(ICodeStatementVisitor<T> visitor)
    {
        return visitor.VisitLambdaStatement(this);
    }
}