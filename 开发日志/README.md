# 开发日志说明

本目录记录每天实际完成的开发事项、验证结果、风险和下一步。

## 规则

- 每天一个文件：`YYYY-MM-DD.md`；
- 只有发生项目活动的日期才创建；
- 不修改过去日志掩盖问题，更正时注明原因；
- 不写真实密码、验证码、令牌或完整剪贴板；
- 正式需求和规范必须更新到 `docs`，不能只存在日志中。

## 辅助命令

```powershell
# 创建或查看今天的日志
.\scripts\Update-DevLog.ps1

# 追加记录
.\scripts\Update-DevLog.ps1 -Completed "完成事项"
.\scripts\Update-DevLog.ps1 -Todo "下一步事项"
.\scripts\Update-DevLog.ps1 -Verification "测试命令与结果" -Risk "风险说明"
```

后续AI或开发人员每次工作应主动更新日志，不需要用户重复提醒。
