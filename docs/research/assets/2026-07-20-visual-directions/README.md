# 2026-07-20 视觉方向样张生成记录

三张图片由 Codex 内置 `imagegen` 生成，均为概念方向比较图，不是最终游戏资产。

## 共同提示结构

- Use case：`stylized-concept`
- Asset type：top-down 2D narrative zombie survival management game 的视觉方向样张
- Scene：黄昏时受损乡间房屋中的小型幸存者避难所；破损地面、床、工作台、实用物资、门后普通丧尸轮廓
- Subject：一名手持手电的匿名成年幸存者
- Composition：16:9、俯视三分之四角度、可读的房间与步行区域、无 UI
- Lighting：室外冷蓝灰、室内克制暖灯和手电锥
- Constraints：同一核心场景；可信比例；无文字、商标、水印；不使用具名角色；不新增叙事事件；不过度血腥；不增加人物或丧尸数量

## A：direction-a-gritty-pixel.png

差异提示：

```text
authentic hand-crafted high-detail pixel art; clearly visible intentional pixel clusters;
limited 24-color palette; crisp nearest-neighbor edges; no smooth painterly gradients;
charcoal, dirty slate blue, faded olive, bone gray, tiny amber highlights;
avoid cute chibi proportions, neon colors, glossy mobile-game rendering,
isometric diamond grid and photorealism
```

## B：direction-b-charcoal-cutout.png

差异提示：

```text
hand-painted 2D illustration designed for layered cutout animation;
charcoal drawing, dry brush, scratched ink, weathered paper texture;
restrained semi-realistic anatomy; clear separable body and environment silhouettes;
avoid direct imitation of any named game or artist, anime, clean vector art,
glossy mobile-game rendering and photorealistic 3D
```

## C：direction-c-prerendered-lowpoly.png

差异提示：

```text
deliberately simplified low-poly 3D models with hand-painted textures;
hard-edged faceted forms, subtle ink contour post-process, baked ambient occlusion;
designed for repeatable multi-angle sprite rendering; orthographic camera;
avoid polished AAA realism, glossy mobile-game rendering, voxel art,
bright saturated colors and isometric diamond grid
```
