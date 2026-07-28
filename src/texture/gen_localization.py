# -*- coding: utf-8 -*-
"""Generate 7DTD Localization.csv (survival + creative) with all 13 languages
   and correct RFC-CSV quoting (fields with commas get quoted, like vanilla)."""
import csv, sys

LANGS = ["english","german","spanish","french","italian","japanese","koreana",
         "polish","brazilian","russian","turkish","schinese","tchinese"]

HEADER = ["Key","File","Type","UsedInMainMenu","NoTranslate","KeepLoaded",
          "english","Context / Alternate Text","german","spanish","french","italian",
          "japanese","koreana","polish","brazilian","russian","turkish","schinese","tchinese"]

# key -> (File, Type, {lang: text})
T = {
 "adamantShapes:VariantHelper": ("blocks","Variant", {
    "english":"Adamant Block","german":"Adamant-Block","spanish":"Bloque de Adamant",
    "french":"Bloc d'Adamant","italian":"Blocco di Adamant","japanese":"アダマントブロック",
    "koreana":"아다만트 블록","polish":"Blok adamantu","brazilian":"Bloco de Adamant",
    "russian":"Адамантовый блок","turkish":"Adamant Blok","schinese":"精金块","tchinese":"精金塊"}),

 "adamantBlockGroupDesc": ("blocks","Block", {
    "english":"Only tools can mine it. Weapons, zombies and explosions deal no damage. Extreme stability.",
    "german":"Nur mit Werkzeugen abbaubar. Waffen, Zombies und Explosionen richten keinen Schaden an. Extreme Stabilität.",
    "spanish":"Solo se puede minar con herramientas. Armas, zombis y explosiones no causan daño. Estabilidad extrema.",
    "french":"Minable uniquement avec des outils. Les armes, les zombies et les explosions n'infligent aucun dégât. Stabilité extrême.",
    "italian":"Estraibile solo con gli attrezzi. Armi, zombie ed esplosioni non causano danni. Stabilità estrema.",
    "japanese":"ツールでのみ採掘可能。武器・ゾンビ・爆発ではダメージを受けない。非常に高い安定性。",
    "koreana":"도구로만 채굴 가능. 무기, 좀비, 폭발로는 피해를 입지 않음. 극도로 높은 안정성.",
    "polish":"Można wydobyć tylko narzędziami. Broń, zombie i eksplozje nie zadają obrażeń. Ekstremalna stabilność.",
    "brazilian":"Só pode ser minerado com ferramentas. Armas, zumbis e explosões não causam dano. Estabilidade extrema.",
    "russian":"Добывается только инструментами. Оружие, зомби и взрывы не наносят урона. Экстремальная устойчивость.",
    "turkish":"Yalnızca aletlerle kazılabilir. Silahlar, zombiler ve patlamalar hasar vermez. Aşırı yüksek stabilite.",
    "schinese":"只能用工具开采。武器、僵尸和爆炸都无法造成伤害。极高稳定性。",
    "tchinese":"只能用工具開採。武器、殭屍和爆炸都無法造成傷害。極高穩定性。"}),

 "adamantSpikesTrap": ("blocks","Trap", {
    "english":"Adamant Spikes Trap","german":"Adamant-Stachelfalle",
    "spanish":"Trampa de pinchos de Adamant","french":"Piège à pieux en Adamant",
    "italian":"Trappola con spuntoni di Adamant","japanese":"アダマントスパイクトラップ",
    "koreana":"아다만트 가시 트랩","polish":"Pułapka — kolce z adamantu",
    "brazilian":"Armadilha de Espinhos de Adamant","russian":"Ловушка с адамантовыми кольями",
    "turkish":"Dikenli Adamant Tuzak","schinese":"精金尖刺陷阱","tchinese":"精金尖刺陷阱"}),

 "adamantSpikesTrapDesc": ("blocks","Trap", {
    "english":"Hurts far more than iron spikes and never wears out. Immune to zombies, weapons and explosions; only tools can remove it.",
    "german":"Richtet deutlich mehr Schaden an als Eisenstacheln und nutzt sich nie ab. Immun gegen Zombies, Waffen und Explosionen; nur mit Werkzeugen entfernbar.",
    "spanish":"Hace mucho más daño que los pinchos de hierro y nunca se desgasta. Inmune a zombis, armas y explosiones; solo se puede quitar con herramientas.",
    "french":"Inflige bien plus de dégâts que les pieux en fer et ne s'use jamais. Insensible aux zombies, aux armes et aux explosions ; seuls les outils peuvent l'enlever.",
    "italian":"Infligge molti più danni degli spuntoni di ferro e non si consuma mai. Immune a zombie, armi ed esplosioni; rimovibile solo con gli attrezzi.",
    "japanese":"鉄スパイクよりはるかに高いダメージを与え、決して摩耗しない。ゾンビ・武器・爆発を受け付けず、ツールでのみ撤去できる。",
    "koreana":"철제 가시보다 훨씬 큰 피해를 주며 절대 마모되지 않는다. 좀비, 무기, 폭발에 면역이며 도구로만 제거할 수 있다.",
    "polish":"Zadaje znacznie większe obrażenia niż żelazne kolce i nigdy się nie zużywa. Odporna na zombie, broń i eksplozje; usuwalna tylko narzędziami.",
    "brazilian":"Causa muito mais dano que espinhos de ferro e nunca se desgasta. Imune a zumbis, armas e explosões; só pode ser removida com ferramentas.",
    "russian":"Наносит намного больше урона, чем железные колья, и никогда не изнашивается. Неуязвима для зомби, оружия и взрывов; убрать можно только инструментом.",
    "turkish":"Demir dikenlerden çok daha fazla hasar verir ve asla aşınmaz. Zombilere, silahlara ve patlamalara karşı bağışıklıdır; yalnızca aletlerle kaldırılabilir.",
    "schinese":"伤害远高于铁钉陷阱，且永不损耗。免疫僵尸、武器和爆炸，只能用工具拆除。",
    "tchinese":"傷害遠高於鐵釘陷阱，且永不損耗。免疫殭屍、武器和爆炸，只能用工具拆除。"}),

# No txName_* entry: the block texture is injected into the atlas by the DLL and is
# deliberately not registered as a paint, so nothing shows it a name (see AdamantAtlas.cs).

 "adamantOre": ("items","Item", {
    "english":"Adamant Ore","german":"Adamant-Erz","spanish":"Mineral de Adamant",
    "french":"Minerai d'Adamant","italian":"Minerale di Adamant","japanese":"アダマント鉱石",
    "koreana":"아다만트 광석","polish":"Ruda adamantu","brazilian":"Minério de Adamant",
    "russian":"Адамантовая руда","turkish":"Adamant Cevheri","schinese":"精金矿石","tchinese":"精金礦石"}),

 "adamantOreDesc": ("items","Item", {
    "english":"A rare crystalline ore. Smelt it with steel to refine Adamant.",
    "german":"Ein seltenes kristallines Erz. Mit Stahl verhüttet ergibt es Adamant.",
    "spanish":"Un mineral cristalino poco común. Fúndelo con acero para refinar Adamant.",
    "french":"Un minerai cristallin rare. Fondez-le avec de l'acier pour raffiner l'Adamant.",
    "italian":"Un raro minerale cristallino. Fondilo con l'acciaio per raffinare l'Adamant.",
    "japanese":"希少な結晶質の鉱石。鋼と一緒に精錬してアダマントを作る。",
    "koreana":"희귀한 결정질 광석. 강철과 함께 제련하여 아다만트를 정제한다.",
    "polish":"Rzadka krystaliczna ruda. Przetop ją ze stalą, aby otrzymać adamant.",
    "brazilian":"Um minério cristalino raro. Funda-o com aço para refinar Adamant.",
    "russian":"Редкая кристаллическая руда. Переплавьте её со сталью, чтобы получить адамант.",
    "turkish":"Nadir kristal bir cevher. Adamant elde etmek için çelikle birlikte eritin.",
    "schinese":"稀有的结晶矿石。与钢一起熔炼可精炼出精金。",
    "tchinese":"稀有的結晶礦石。與鋼一起熔煉可精煉出精金。"}),

 "adamantIngot": ("items","Item", {
    "english":"Adamant Ingot","german":"Adamant-Barren","spanish":"Lingote de Adamant",
    "french":"Lingot d'Adamant","italian":"Lingotto di Adamant","japanese":"アダマントインゴット",
    "koreana":"아다만트 주괴","polish":"Sztabka adamantu","brazilian":"Lingote de Adamant",
    "russian":"Адамантовый слиток","turkish":"Adamant Külçe","schinese":"精金锭","tchinese":"精金錠"}),

 "adamantIngotDesc": ("items","Item", {
    "english":"Refined Adamant. One ingot crafts one Adamant block.",
    "german":"Verfeinertes Adamant. Ein Barren ergibt einen Adamant-Block.",
    "spanish":"Adamant refinado. Un lingote fabrica un bloque de Adamant.",
    "french":"Adamant raffiné. Un lingot permet de fabriquer un bloc d'Adamant.",
    "italian":"Adamant raffinato. Un lingotto crea un blocco di Adamant.",
    "japanese":"精錬されたアダマント。インゴット1個でアダマントブロックを1個作れる。",
    "koreana":"정제된 아다만트. 주괴 1개로 아다만트 블록 1개를 제작한다.",
    "polish":"Oczyszczony adamant. Jedna sztabka tworzy jeden blok adamantu.",
    "brazilian":"Adamant refinado. Um lingote cria um bloco de Adamant.",
    "russian":"Очищенный адамант. Один слиток создаёт один адамантовый блок.",
    "turkish":"Rafine edilmiş Adamant. Bir külçe, bir Adamant blok üretir.",
    "schinese":"精炼后的精金。一锭可制作一个精金块。",
    "tchinese":"精煉後的精金。一錠可製作一個精金塊。"}),
}

def row(key):
    f, typ, tr = T[key]
    r = [key, f, typ, "", "", "", tr["english"], ""]
    for lang in LANGS[1:]:
        r.append(tr[lang])
    return r

def write(path, keys):
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh, quoting=csv.QUOTE_MINIMAL)
        w.writerow(HEADER)
        for k in keys:
            w.writerow(row(k))
    print("wrote", path)

survival_keys = list(T.keys())
creative_keys = ["adamantShapes:VariantHelper","adamantBlockGroupDesc",
                 "adamantSpikesTrap","adamantSpikesTrapDesc"]
write("Localization_survival.csv", survival_keys)
write("Localization_creative.csv", creative_keys)
