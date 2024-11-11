import { formatDuration } from 'date-fns'
import { addThousandsSeparator, removeTrailingZeroes } from '@/utils/stringHelpers'

export function yesNo(value: boolean | null | undefined) {
  switch (value) {
    case true:
      return "Yes"
    case false:
      return "No"
    default:
      return "Unknown"
  }
}

export function formatNumber(value: number): string {
  if (value === 0) {
    return '0'
  }

  const absoluteValue = Math.abs(value)
  let exponent = 0

  // Handle large values
  if (absoluteValue >= 1e24) {
    exponent = 24
  } else if (absoluteValue >= 1e21) {
    exponent = 21
  } else if (absoluteValue >= 1e18) {
    exponent = 18
  } else if (absoluteValue >= 1e15) {
    exponent = 15
  } else if (absoluteValue >= 1e12) {
    exponent = 12
  } else if (absoluteValue >= 1e9) {
    exponent = 9
  } else if (absoluteValue >= 1e6) {
    exponent = 6
  }

  // Handle small values
  if (absoluteValue <= 1e-21) {
    exponent = -21
  } else if (absoluteValue <= 1e-18) {
    exponent = -18
  } else if (absoluteValue <= 1e-15) {
    exponent = -15
  } else if (absoluteValue <= 1e-12) {
    exponent = -12
  } else if (absoluteValue <= 1e-9) {
    exponent = -9
  } else if (absoluteValue <= 1e-6) {
    exponent = -6
  } else if (absoluteValue <= 1e-3) {
    exponent = -3
  }

  let scaledValue = value
  if (exponent > 0) {
    scaledValue = value / 10 ** exponent
  }
  if (exponent < 0) {
    scaledValue = value * 10 ** (exponent + 3)
  }

  let abbreviatedValue
  const theInputWasScaled = exponent !== 0
  if (theInputWasScaled) {
    abbreviatedValue = scaledValue.toPrecision(4)
  } else {
    abbreviatedValue = scaledValue.toString().substring(0, 6)
  }

  abbreviatedValue = removeTrailingZeroes(abbreviatedValue)
  abbreviatedValue = addThousandsSeparator(abbreviatedValue)

  if (exponent === 0) {
    return abbreviatedValue
  }
  return `${value < 0 ? '-' : ''}${abbreviatedValue} · ${10}^${exponent}`
}

export function duration(value: string | null | undefined) {
  if (!value) {
    return "Unknown"
  }
  const iso8601DurationRegex = /(-)?P(?:([.,\d]+)Y)?(?:([.,\d]+)M)?(?:([.,\d]+)W)?(?:([.,\d]+)D)?(?:T(?:([.,\d]+)H)?(?:([.,\d]+)M)?(?:([.,\d]+)S)?)?/
  const matches = value.match(iso8601DurationRegex)
  if (!matches) {
    return "Invalid"
  }
  if (!matches.some(m => Number.parseInt(m) > 0)) {
    return "None"
  }
  const duration = {
    years: Number.parseInt(matches[2]) ?? 0,
    months: Number.parseInt(matches[3]) ?? 0,
    weeks: Number.parseInt(matches[4]) ?? 0,
    days: Number.parseInt(matches[5]) ?? 0,
    hours: Number.parseInt(matches[6]) ?? 0,
    minutes: Number.parseInt(matches[7]) ?? 0,
    seconds: Number.parseInt(matches[8]) ?? 0
  }

  return formatDuration(duration)
}
