export interface ConsumerTypeFraction {
  consumer_type: ConsumerCategory
  power_fraction: number
}

export enum ConsumerCategory {
  agriculture = 'agriculture',
  household = 'household',
  industry = 'industry',
  services = 'services',
  public = 'public',
  elind = 'elind',
  undefined = 'undefined',
}
