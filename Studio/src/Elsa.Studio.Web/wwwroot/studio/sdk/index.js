function r() {
  const t = [];
  return {
    add(n) {
      t.push(n);
    },
    list() {
      return [...t];
    }
  };
}
export {
  r as createContributionRegistry
};
