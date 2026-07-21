# Dead Signal 世界材质资产

## gritty-material-overlay.png

- 用途：`IsoTilePanel` 的通用可染色材质层；营地和全部探索关共用。
- 规格：1024×1024，灰度 PNG，水平/垂直镜像无缝，运行时最近邻采样并重复平铺。
- 来源：OpenAI 内置 image generation，2026-07-20 生成；项目原创资产，无第三方素材。
- 后处理：镜像拼接保证边缘无缝，缩放到 1024×1024，灰度化并限制到低对比度亮灰范围，供既有色板乘色。
- 最终 prompt：

  > Use case: stylized-concept. Asset type: production game texture, seamless grayscale material overlay for a faux-isometric 2D survival game. Reference image establishes the gritty pixel-art rendering language only. Create one perfectly tileable square grayscale pixel-art surface texture that can be tinted at runtime and reused on dirt, concrete, wood, brick, metal, and worn interior surfaces. Flat texture swatch only, no scene and no perspective. Restrained hand-authored-looking pixel art, crisp nearest-neighbor pixel clusters, subtle grime, scratches, chips, mottling and wear. Edge-to-edge square seamless texture, uniform detail density, neutral flat lighting, mid-gray base, low contrast. No objects, symbols, text, borders, perspective, directional shadows, watermark or large focal cracks.

## ground-decals.png

- 用途：平面 `IsoTilePanel` 的稀疏环境细节图集；仅作视觉，不参与碰撞、寻路、噪音或搜刮。
- 规格：1254×1254 RGBA PNG，4×4 等分图集；运行时排除血迹与弹壳，二者只由真实战斗系统生成。
- 来源：OpenAI 内置 image generation，2026-07-20 生成；项目原创资产，无第三方素材。
- 后处理：以边缘自动采样的洋红色键抠出透明通道，soft matte + despill；已验证透明角与主体覆盖。
- 最终 prompt：

  > Use case: stylized-concept. Asset type: production 2D game sprite atlas for faux-isometric ground decals. Reference image establishes the gritty dark pixel-art style and 2:1 faux-isometric camera only. Create exactly sixteen small independent environmental ground decals arranged in a precise 4 by 4 grid: chipped stones, tiny rubble pile, dead grass tuft, dry leaves, cracked earth patch, broken concrete chips, torn paper scraps, dark oil stain, rust flakes, small wood splinters, ash patch, scattered shell casings, muddy footprint pair, tiny weeds, broken glass shards, and a small blood smear. Perfectly flat solid #ff00ff chroma-key background. Crisp hand-authored pixel art, one centered isolated decal per equal cell, generous padding, no overlap, labels, grid lines, text or watermark.

## actor-directions.png

- 用途：`ActorSprite` 的正式 8 方向泛用角色；4 行依次为幸存者、劫掠者、普通丧尸、犬类，8 列依次为南、西南、西、西北、北、东北、东、东南。
- 规格：512×384 RGBA PNG，单格 64×96；运行时按投影后的自由朝向量化选帧，武器、阵营环、血条、受击和状态条仍由真实状态叠加。
- 来源：OpenAI 内置 image generation，2026-07-20 生成；项目原创资产，无第三方素材。
- 后处理：洋红键抠图后逐格裁切、最近邻缩放并统一脚点；加载失败自动回退旧矢量人形。人类统一现实人体比例。
- 最终 prompt：

  > Revise the generic 4-row by 8-column actor direction atlas so every human figure uses realistic anatomy. Rows 1–3 (generic survivor, hostile raider, ordinary civilian zombie) must be natural approximately 7 to 7.5 heads tall, with smaller heads, longer torsos and legs, realistic shoulder widths; absolutely no chibi, bobblehead or oversized-head proportions. Row 4 remains a generic dog fallback with realistic canine anatomy and consistent scale. Preserve exactly 4 rows and 8 equal columns; columns south, southwest, west, northwest, north, northeast, east, southeast. Preserve each row's identity and restrained gritty clothing palette, empty hands, identical foot anchors. Crisp faux-isometric hand-authored pixel art, solid flat #ff00ff background. No text, labels, borders, grid lines, weapons, props, cast shadows, watermark, extra figures, rows or columns.

## named-actors-a.png / named-actors-b.png

- 用途：7 名 authored 幸存者的正式 8 方向站立图集。A 图依次为山姆、诺蒂、克莉丝汀、耗子；B 图依次为道格、南丁格尔、皮特。
- 规格：A=512×384、B=512×288 RGBA PNG，单格均为 64×96；列顺序与泛用角色图集一致。
- 来源：OpenAI 内置 image generation，2026-07-20 生成；项目原创资产，无第三方素材。
- 后处理：洋红键抠图后逐格裁切、最近邻缩放并统一底部中心脚点。全体使用现实人体比例；耗子为黑色瞳孔，道格完全无胡须/胡茬，南丁格尔有明显皱纹、金色丸子头和利落工作姿态。
- 最终 prompt（A）：

  > Revise the 4-row by 8-column named-character atlas to use realistic human anatomy. All adult characters must have natural approximately 7 to 7.5 heads-tall proportions: distinctly smaller heads, longer torsos and legs, normal shoulder widths, no chibi or bobblehead exaggeration. Preserve exactly 4 rows and 8 columns, same directions and foot anchors. Row 1 Sam is a robust muscular black-haired young man with scruffy beard and American forward fringe; row 2 Notty is a short thin pale young white man with blond hair; row 3 Christine is a thin Black woman in her twenties with dreadlocks tied high; row 4 Rat is an unhealthy gaunt pale white woman around thirty with greasy slightly wavy long black hair and clearly black pupils/irises in every visible eye. Keep clothing palettes and empty-handed neutral idle poses. Gritty faux-isometric hand-authored pixel art, solid #ff00ff background. No text, labels, borders, grid lines, weapons, props, shadows, watermark, extra people, rows or columns.
- 最终 prompt（B）：

  > Create the final revision of the 3-row by 8-column named-character atlas, matching realistic anatomy. Adults must be approximately 7 to 7.5 heads tall; Pete approximately 6.5 to 7 heads tall. Row 1 Doug: proportionately built white man in his forties, red pompadour/quiff, completely clean-shaven with absolutely no beard, moustache or stubble. Row 2 Nightingale: tall proportionately built white woman in her fifties, visibly mature with clear facial wrinkles, golden-blonde hair tied into a neat practical bun, alert upright posture and practical work-ready clothing that makes her look highly efficient and capable at physical work; not frail, not young, not glamorous. Row 3 Pete: sixteen-year-old male high-school student, proportionate teenage build, short brown hair. Exactly 3 rows and 8 directions, identical foot anchors, empty hands, solid #ff00ff background, no text, borders, props or extra figures.

## bruce-directions.png

- 用途：布鲁斯专属 8 方向站立图集；明确为纯种成年德国牧羊犬。
- 规格：512×96 RGBA PNG，单格 64×96，列顺序与角色图集一致。
- 来源：OpenAI 内置 image generation，2026-07-20 生成；项目原创资产，无第三方素材。
- 最终 prompt：

  > Create exactly 1 row and exactly 8 equal columns. The single character is Bruce, a purebred adult German Shepherd dog, not a shepherd mix: recognizable black saddle and mask, rich tan legs and chest, erect pointed ears, long muzzle, athletic proportionate working-dog build, bushy lowered tail. Columns are south/front, southwest, west/left profile, northwest, north/back, northeast, east/right profile, southeast. Preserve identical scale, anatomy, coat pattern, collar, and foot anchors. Static alert neutral standing poses, no armor, clothing or props. Crisp gritty faux-isometric hand-authored pixel art on solid #ff00ff background, no text, borders, shadows or extra dogs.

## camp-props.png

- 用途：营地正式道具外观。4×4 依次为工作台、收音机、储物柜、衣柜、展示柜、草垛、椅子、座垫、沙袋、床、木箱、货架、桶、油灯、检查床、炉子。
- 规格：384×384 RGBA PNG，单格 96×96，底部中心锚点。
- 来源：OpenAI 内置 image generation，2026-07-20 生成；项目原创资产，无第三方素材。
- 后处理：洋红键抠图后逐格裁切、最近邻缩放并统一脚点；只替换视觉，玩法几何仍由原节点负责。
- 最终 prompt：

  > Use case: stylized-concept. Asset type: production faux-isometric pixel-art camp prop atlas. Create exactly sixteen isolated props in a precise 4 by 4 grid: workbench, radio, storage cabinet, wardrobe, glass display cabinet, hay bale, chair, padded stool, sandbag barricade, single bed, wooden crate, utility shelf, rusty barrel, oil lamp, examination table, and compact stove. Solid flat #ff00ff chroma-key background, one centered prop per equal cell with generous padding and consistent bottom anchors. Gritty restrained survival-game palette, crisp hand-authored pixel art, no people, labels, text, borders, grid lines, shadows or watermark.

## held-equipment-directions.png

- 用途：paper-doll 第一批真实手持物；7 行依次为匕首、长剑、手枪、步枪、长弓、手电、火把，8 列依次为南、西南、西、西北、北、东北、东、东南。
- 规格：768×672 RGBA PNG，单格 96×96；运行时从 `Pawn.WeaponInHand` / `HeldLight` 读取真实左右手状态，双手武器去重、双持分别绘制、背向三方向画在身体后方。
- 来源：OpenAI 内置 image generation，2026-07-20 生成；项目原创资产，无第三方素材。
- 后处理：洋红键抠图后逐格裁切、最近邻缩放并统一到 96×96；透明边和 7×8 数量已验证。熄灭火把不使用带火焰帧，回退为无焰木杆。
- 最终 prompt：

  > Use case: stylized-concept. Asset type: production 2D held-equipment direction atlas for the existing gritty faux-isometric pixel-art survival game. Create exactly 7 rows and exactly 8 equal columns. Columns in every row are the equipment orientations for a character facing south/front, southwest, west, northwest, north/back, northeast, east, southeast. Rows are exactly: compact survival dagger; long two-handed European-style sword; practical semi-automatic handgun; worn full-length hunting/military rifle; tall wooden longbow; compact metal handheld flashlight; burning wooden torch with a small crisp pixel flame but no glow aura. Each cell contains only the isolated item, oriented exactly as it would appear in the hands of the matching character direction, with the grip/pivot centered consistently and realistic relative size. Crisp hand-authored pixel art matching the references, perfectly flat solid #ff00ff background, no shadows, glow haze, hands, arms, people, arrows, ammunition, text, labels, borders, grid lines, watermark or extra objects.

## equipment-paper-doll-60

- 用途：第二批正式装备可视化，恰好 60 件＝剩余 22 件武器、Wiki 护甲表 33 件人类穿戴、布鲁斯 5 件装备；不含改装件，也不把额外纯覆盖品“防毒面具”冒充进 60 件。
- 文件：`held-equipment-{b,c,d}.png`、`held-{fire-axe,rapier,military-rifles}.png`；`apparel-{base,outer,special,head,glove,war-mask,plate}.png`；`dog-apparel.png`、`dog-pocket-harness.png`。
- 来源：OpenAI 内置 image generation，2026-07-20 生成；项目原创资产，无第三方素材。生成模式为内置图像生成；人物/布鲁斯既有图集仅作比例、相机与方向参考。
- 后处理：统一以 `#ff00ff` 色键去底并清理溢色，再用最近邻缩放到 96×96 附件格或 64×96 身体叠层格；原始生成文件保留在 Codex 生成目录。
- prompt 组：三张武器图按 8 方向分别指定 8/8/6 行精确武器清单；消防斧因首稿混入骨刀/短柄手斧而用独立长柄红色破拆斧行替换；刺剑独立修成细长迅捷剑式直刃与精细护手；步枪/狙击枪独立修成无木材的黑色军用聚合物制式枪。人类衣物按贴身裤装、外套装甲、特殊装甲鞋具、头面附件、单只手套分组；狗装备按德牧身体对齐生成。修订 prompt 明确：战争/恐怖面具和所有眼镜不得覆盖发型；板甲覆盖躯干与腿但不得出现铁手套、鞋或脚甲；口袋狗衣只能由固定带和两侧多个独立小布袋构成，不能用整块布覆盖躯干。
- 运行时：`EquipmentVisualCatalog` 登记图集路径/行/层/锚点；`ActorSprite` 只读真实武器、11 个穿戴槽与狗装备槽。成对鞋/手套逐侧绘制，多槽装备去重，面部附件使用低于发际线的 Face 锚点。
