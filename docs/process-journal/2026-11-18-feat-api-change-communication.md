2026-11-18 · decision · CliTree · System.CommandLine writes nothing for a hidden command's own --help, so a deprecated command's sunset could show nowhere. The help action lifts Hidden for the length of that one help, beside the description swap. A command hidden by cli.json alone still writes no help. (self)

2026-11-18 · review · CliTree · The empty own help of a command hidden by cli.json alone was left as a draft item on #20. Jonas asked for it fixed in the same PR, with a criterion of its own.
