# 配置Post-Build自动注册

## 方法1：通过Visual Studio UI配置（推荐）

1. **右键点击项目** → **属性**
2. **选择"生成事件"选项卡**
3. **在"生成后事件命令行"中输入：**

```
"$(ProjectDir)PostBuild.bat" "$(TargetDir)" "$(TargetName)" "$(ConfigurationName)"
```

4. **点击"确定"保存**

## 方法2：手动编辑项目文件

如果你更喜欢直接编辑项目文件，在WordTools.csproj的最后（在`</Project>`标签之前）添加：

```xml
<Target Name="PostBuild" AfterTargets="PostBuildEvent">
  <Exec Command="&quot;$(ProjectDir)PostBuild.bat&quot; &quot;$(TargetDir)&quot; &quot;$(TargetName)&quot; &quot;$(ConfigurationName)&quot;" />
</Target>
```

## 工作原理

- **PostBuild.bat** 脚本会在每次编译后自动运行
- **只在Debug配置下**自动注册，避免Release编译时的干扰
- **自动检测系统架构**（32位/64位）选择正确的RegAsm
- **先卸载旧版本再注册新版本**，确保使用最新代码
- **错误处理**：如果没有管理员权限，会提示手动运行注册脚本

## 使用体验

配置完成后：

1. **编译项目** → 自动注册插件
2. **重启Word** → 查看更新后的插件
3. **调试更方便** → 不需要每次手动运行bat文件

## 注意事项

- 需要**管理员权限**才能自动注册COM组件
- 如果Visual Studio不是以管理员身份运行，Post-build会失败但不影响编译
- 失败时可以手动运行 `RegisterPlugin.bat`