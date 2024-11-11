
export function getLastChar(str: string | undefined) {
  return str?.substring(str.length - 1)
}

export function removeLastChar(str: string | undefined) {
  return str?.substring(0, str.length - 1) ?? ''
}

/// Remove trailing zeroes from the decimal portion of a number
export function removeTrailingZeroes(value: string | number): string {
  if (typeof value === 'number') {
    value = value.toString()
  }

  let [integerPart, decimalPart] = value.split('.')
  if (decimalPart !== undefined) {
    while (getLastChar(decimalPart) === '0') {
      decimalPart = removeLastChar(decimalPart)
    }
  }

  return decimalPart
    ? `${integerPart}.${decimalPart}`
    : integerPart
}

/// Works for numbers less than 10^6
export function addThousandsSeparator(value: string | number): string {
  if (typeof value === 'number') {
    value = value.toString()
  }

  const separator = ' '

  let [integerPart, decimalPart] = value.split('.')
  if (integerPart === undefined) {
    return value
  }

  if (integerPart.length > 3) {
    const charArray = integerPart.split('')
    charArray.splice(integerPart.length - 3, 0, separator)
    integerPart = charArray.join("")
  }

  return decimalPart
    ? `${integerPart}.${decimalPart}`
    : integerPart
}
