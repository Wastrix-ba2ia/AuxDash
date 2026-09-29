# 发布到 GitHub

1. 使用本次导出的 `dist/NeonSideScreenMonitor-source` 文件夹；不要发布当前运行目录或上级工作目录。
2. 在 GitHub 创建空仓库，建议名称 `neon-side-screen-monitor`。
3. 使用 GitHub Desktop 添加导出文件夹，创建本地仓库，检查 Changes 后提交。
4. 点击 Publish repository；确认要公开时取消 Keep this code private。
5. 源码不含完整人物素材。完整安装包应在确认素材和第三方组件分发权限后另做 GitHub Release。

也可以在导出的文件夹中运行：

```powershell
git init -b main
git add .
git diff --cached --stat
git commit -m "Initial public source release"
git remote add origin https://github.com/YOUR_NAME/neon-side-screen-monitor.git
git push -u origin main
```

替换 YOUR_NAME。提交前确认无 bindings.xml、settings.xml、pubg-profile.xml、认证文件、密钥、会话、缓存、日志以及视频生成任务记录。
本次附 MIT 许可证草案，发布前确认采用该许可证及版权署名。

官方指南：https://docs.github.com/en/migrations/importing-source-code/using-the-command-line-to-import-source-code/adding-locally-hosted-code-to-github
