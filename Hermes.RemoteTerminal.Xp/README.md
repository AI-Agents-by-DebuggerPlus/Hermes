# Hermes.RemoteTerminal.Xp — резервный мост

**Резерв** к основному `Hermes.RemoteTerminal` (.NET 8).

Использовать, когда основной клиент не может стабильно ходить в Supabase (Windows XP, нет TLS 1.2 в Schannel — тогда `tools\curl.exe`).

Стек как у `Hermes.EnglishLearning.Xp`: REST poll + XpHttp/Curl.

Основной терминал: `../Hermes.RemoteTerminal/`  
Док: `Docs/Reports/RemoteTerminal/README.md`
