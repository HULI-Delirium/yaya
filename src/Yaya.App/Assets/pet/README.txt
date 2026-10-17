宠物图交接说明（美术同学 / 未来的你）

放置 6 张 PNG 到本目录（与 exe 同级发布，文件名固定，缺一不可——缺失时程序会
自动回退到 XAML 矢量占位图并在日志中点名，不会崩溃）：

  pet_healthy.png   健康待机（眨眼图临时切换用 pet_blink.png）
  pet_blink.png     眨眼帧
  pet_wilt_a.png    开始蔫（Healthy → Wilted 迁移时的起始帧）
  pet_wilt_b.png    彻底蔫（Wilted 的稳态帧）
  pet_weeds.png     逃避后长草（标记“在逃避”时显示，回到学习恢复）
  pet_bloom.png     开花彩蛋（标记“有效查资料”时显示 3 秒）

规格约定（重要）：
  - 1024 x 1024 像素，透明底 PNG；
  - 统一锚点：盆底中心 = 图片底边中点。所有旋转/缩放动画都以该点为轴心；
  - 六张图的角色轮廓在画面中的位置尽量一致，避免切换时跳动。

换图不改代码：替换本目录（或发布目录 Assets\pet\）下的同名文件即可。


开发调试（默认关闭）

宠物窗 F6–F9 调试热键由条件编译符号 DEBUG_HOTKEYS 控制，默认构建不包含。
开启方式（二选一）：

  1. 在 src/Yaya.App/Yaya.App.csproj 的 PropertyGroup 中加一行：
       <DefineConstants>$(DefineConstants);DEBUG_HOTKEYS</DefineConstants>
  2. 临时整行命令构建：
       dotnet build -p:DefineConstants="DEBUG;TRACE;DEBUG_HOTKEYS"

热键（需先点一下宠物窗获取键盘焦点）：
  F6 模拟回到学习（Healthy）   F7 模拟游离（Wilted）
  F8 模拟"有效查资料"（Bloom） F9 模拟"在逃避"（Weeds）

正式发布前应在 csproj 中移除该符号（见 PetWindow.xaml.cs 顶部注释）。
