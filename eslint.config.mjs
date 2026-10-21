// {} の省略を禁止する（if / else / for / while / do の本文。1 行で済む本文も同じ）。
// 他の規則は入れない（この設定は、省略の禁止だけのためにある）。
import tseslint from 'typescript-eslint';

export default [
  { ignores: ['node_modules/**', '_reference/**', 'src/**', 'tests/**', '_development/**'] },
  {
    files: ['**/*.ts'],
    languageOptions: { parser: tseslint.parser },
    rules: { curly: ['error', 'all'] },
  },
];
