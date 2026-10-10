using SlotMath.Core.Expressions;
namespace SlotMath.Core.Measurements;

public static class ExitClassification
{
    public static string Read(ExprValue value) => value.Kind == ExprType.String && value.StringValue is "condition" or "modelLimit" or "payoutCap" or "authoredStop" or "resourceExpiry"
        ? value.StringValue : throw new InvalidOperationException("Exit classification must be condition, modelLimit, payoutCap, authoredStop or resourceExpiry.");
}
