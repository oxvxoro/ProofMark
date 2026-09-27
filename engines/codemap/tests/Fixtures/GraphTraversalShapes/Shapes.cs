namespace Fixture;

// 다이아몬드: A -> B, A -> C, B -> D, C -> D. D는 서로 다른 깊이 2 경로 두 개
// (B를 거치는 경로와 C를 거치는 경로)로 A에서 도달하므로, 경로별 via-edge
// 증거를 유지하는 재귀 순회는 하나로 합치지 않으면 깊이 2의 D 행을 두 개 만들 수 있다.
public class Diamond
{
    public void A() { B(); C(); }
    public void B() { D(); }
    public void C() { D(); }
    public void D() { }
}

// 순환: A -> B -> C -> A. 순회는 무한 루프 없이 MaxDepth에서 끝나야 한다.
public class Cycle
{
    public void A() { B(); }
    public void B() { C(); }
    public void C() { A(); }
}

// 같은 소스 줄에서 같은 대상을 호출하는 서로 다른 호출 두 개. 후보 엣지가
// source/target/kind는 같고 열만 다를 때 via-edge 선택을 검증한다.
public class DuplicateCallsSameLine
{
    private readonly Diamond diamond = new();
    public void CallTwice() { diamond.A(); diamond.A(); }
}
