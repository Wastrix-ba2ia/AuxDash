# GitHub 发布

完整运行版发布到 Releases，并始终附带 `AuxDash-vX.Y.Z-windows-x64-full.zip`。该完整包默认收录所有带 `ready.txt` 且含 `approved-frames` 的人物动画素材，以及当前运行所需的桥接程序和传感器依赖。

更新源码仓库时，只提交应用源文件、文档、字幕 JSON 和素材生成/构建工具；不要提交 `.exe`、账号绑定、设置、API 密钥、日志或运行缓存。GitHub 自动生成的 Source code ZIP 不含媒体素材。普通用户通过 README 的 Release 链接下载完整 ZIP。

发布前从全新目录重新构建、打完整包，检查媒体清单、文件总量和压缩包内容，然后创建带完整包附件的 GitHub Release。以后沿用这一流程，默认把审核完成的人物动画放进完整包。
