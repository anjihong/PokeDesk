namespace DeskPokemon;

public enum EggKind { Common, Rare, Epic, Legendary, Shiny }

/// <summary>대기 시작에 확정한 결과. 부화는 재추첨 없이 이 값을 지급한다.</summary>
public sealed record PendingEgg(EggKind Kind, int Dex, bool IsShiny);
