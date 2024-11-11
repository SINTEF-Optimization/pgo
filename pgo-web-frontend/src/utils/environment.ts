const environment = {
  get isDevelopment() {
    return process.env.NODE_ENV === 'development'
  },
}

export default environment
