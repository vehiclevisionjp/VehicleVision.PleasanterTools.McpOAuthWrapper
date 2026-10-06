import { execFileSync } from "node:child_process";
import { readFile, writeFile } from "node:fs/promises";
import { lint } from "markdownlint/promise";
import { applyFixes } from "markdownlint";

// Git の追跡対象だけを扱い、サブモジュール・一時成果物・node_modules を読まない。
const files = execFileSync("git", ["ls-files", "-z", "--", "*.md"], { encoding: "utf8" }).split("\0").filter(Boolean);
const config = JSON.parse(await readFile(".markdownlint.json", "utf8"));
let results = await lint({ files, config });
if (process.argv.includes("--fix")) {
  for (const file of files) {
    if (results[file].length) await writeFile(file, applyFixes(await readFile(file, "utf8"), results[file]), "utf8");
  }
  results = await lint({ files, config });
}
const errors = Object.values(results).flat().length;
if (errors) {
  for (const [file, problems] of Object.entries(results)) {
    for (const problem of problems) console.error(`${file}:${problem.lineNumber} ${problem.ruleNames[0]} ${problem.ruleDescription}${problem.errorDetail ? ": " + problem.errorDetail : ""}`);
  }
  process.exitCode = 1;
}
else console.log(`Markdown lint: ${files.length} files, 0 errors`);
