using NUnit.Framework;
using UnityEngine;

/// 3.1·3.2 부품 불량 적용 검증: ApplyDefect(None)이면 모두 보이고, PartLoss이면 지정한 부품 하나만 꺼진다
public class PartLossTests
{
    private GameObject root;
    private GameObject[] parts;
    private PartLoss partLoss;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Part");
        parts = new GameObject[3];
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = new GameObject($"Finger{i}");
            parts[i].transform.SetParent(root.transform);
        }

        partLoss = root.AddComponent<PartLoss>();
        partLoss.removableParts = parts;
        partLoss.removeIndex = 1;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
    }

    [Test]
    public void ApplyDefect_None_KeepsAllPartsActive()
    {
        partLoss.ApplyDefect(DefectType.None);

        foreach (var part in parts)
        {
            Assert.IsTrue(part.activeSelf, $"{part.name}가 꺼지면 안 됩니다.");
        }
    }

    [Test]
    public void ApplyDefect_PartLoss_DeactivatesOnlyTargetPart()
    {
        partLoss.ApplyDefect(DefectType.PartLoss);

        Assert.IsTrue(parts[0].activeSelf, "num과 다른 부품은 보여야 합니다.");
        Assert.IsFalse(parts[1].activeSelf, "num번째 부품은 꺼져야 합니다.");
        Assert.IsTrue(parts[2].activeSelf, "num과 다른 부품은 보여야 합니다.");
    }

    [Test]
    public void ApplyDefect_AngleError_KeepsAllPartsActive()
    {
        partLoss.ApplyDefect(DefectType.AngleError);

        foreach (var part in parts)
        {
            Assert.IsTrue(part.activeSelf, $"{part.name}가 꺼지면 안 됩니다.");
        }
    }
}
