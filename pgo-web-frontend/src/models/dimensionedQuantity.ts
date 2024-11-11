export class DimensionedQuantity {
  private readonly initialValue: number | string | null
  private readonly initialScaleMultiplier: number
  private readonly baseUnit: string | undefined

  private prefix = ''
  private scaledAbsoluteValue: number | undefined
  private isNegative = false
  private readonly precision = 4

  constructor(
    initialValue: number | string | null,
    baseUnit: string | undefined = undefined,
    initialScaleMultiplier = 1,
  ) {
    this.initialValue = initialValue
    this.baseUnit = baseUnit
    this.initialScaleMultiplier = initialScaleMultiplier
    this.calcScaledValue()
  }

  private calcScaledValue(): void {
    if (typeof this.initialValue !== 'number') {
      return
    }

    // Handle zero
    if (this.initialValue === 0) {
      this.scaledAbsoluteValue = 0
      return
    }

    // Handle negative values
    this.isNegative = this.initialValue < 0

    this.scaledAbsoluteValue = Math.abs(this.initialValue) * this.initialScaleMultiplier

    // Handle large values
    if (this.scaledAbsoluteValue >= 1e24) {
      this.prefix = 'Y'
      this.scaledAbsoluteValue /= 1e24
    } else if (this.scaledAbsoluteValue >= 1e21) {
      this.prefix = 'Z'
      this.scaledAbsoluteValue /= 1e21
    } else if (this.scaledAbsoluteValue >= 1e18) {
      this.prefix = 'E'
      this.scaledAbsoluteValue /= 1e18
    } else if (this.scaledAbsoluteValue >= 1e15) {
      this.prefix = 'P'
      this.scaledAbsoluteValue /= 1e15
    } else if (this.scaledAbsoluteValue >= 1e12) {
      this.prefix = 'T'
      this.scaledAbsoluteValue /= 1e12
    } else if (this.scaledAbsoluteValue >= 1e9) {
      this.prefix = 'G'
      this.scaledAbsoluteValue /= 1e9
    } else if (this.scaledAbsoluteValue >= 1e6) {
      this.prefix = 'M'
      this.scaledAbsoluteValue /= 1e6
    } else if (this.scaledAbsoluteValue >= 1e3) {
      this.prefix = 'k'
      this.scaledAbsoluteValue /= 1e3
    }

    // Handle small values
    if (this.scaledAbsoluteValue <= 1e-21) {
      this.prefix = 'y'
      this.scaledAbsoluteValue *= 1e24
    } else if (this.scaledAbsoluteValue <= 1e-18) {
      this.prefix = 'z'
      this.scaledAbsoluteValue *= 1e21
    } else if (this.scaledAbsoluteValue <= 1e-15) {
      this.prefix = 'a'
      this.scaledAbsoluteValue *= 1e18
    } else if (this.scaledAbsoluteValue <= 1e-12) {
      this.prefix = 'f'
      this.scaledAbsoluteValue *= 1e15
    } else if (this.scaledAbsoluteValue <= 1e-9) {
      this.prefix = 'p'
      this.scaledAbsoluteValue *= 1e12
    } else if (this.scaledAbsoluteValue <= 1e-6) {
      this.prefix = 'n'
      this.scaledAbsoluteValue *= 1e9
    } else if (this.scaledAbsoluteValue <= 1e-3) {
      this.prefix = 'μ'
      this.scaledAbsoluteValue *= 1e6
    } else if (this.scaledAbsoluteValue < 1) {
      this.prefix = 'm'
      this.scaledAbsoluteValue *= 1e3
    }
  }

  getString() {
    if (this.scaledAbsoluteValue === undefined) {
      return this.initialValue
    }

    if (this.scaledAbsoluteValue === 0) {
      return `${this.scaledAbsoluteValue} ${this.baseUnit}`
    }

    if (this.initialValue === "Infinity") {
      return `Infinity ${this.baseUnit ?? ''}`
    }

    // Shorten the number
    const abbreviatedValue = this.scaledAbsoluteValue.toPrecision(this.precision)

    // Add the unit
    return `${this.isNegative ? '-' : ''}${abbreviatedValue} ${this.prefix}${this.baseUnit ?? ''}`
  }
}
