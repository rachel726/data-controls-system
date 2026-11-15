/** The current value of the input, textarea or select that raised a DOM event. */
export function inputValue(event: Event): string {
  return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
}
