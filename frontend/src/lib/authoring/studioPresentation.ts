export const readable = (name: string) => name.replaceAll('-', ' ').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^\w/, letter => letter.toUpperCase());
export function downloadText(name: string, content: string, type = 'text/csv') {
  const url = URL.createObjectURL(new Blob([content], { type })), link = document.createElement('a'); link.href = url; link.download = name; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}
